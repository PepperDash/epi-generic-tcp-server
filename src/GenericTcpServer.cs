// For Basic SIMPL# Classes
// For Basic SIMPL#Pro classes

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;
using PepperDash.Core.Logging;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using IPAddress = System.Net.IPAddress;
using IPEndPoint = System.Net.IPEndPoint;

namespace PepperDash.Essentials.Plugins
{
	public class GenericTcpServer : EssentialsBridgeableDevice, IDisposable
	{
		private readonly GenericTcpServerPropertiesConfig _config;
		private readonly IPEndPoint _endPoint;
		private readonly TcpListener _server;
		private readonly List<TcpClient> _clients;
		private readonly object _clientsLock = new object();
		private readonly object _stateLock = new object();
		private Thread _listenerThread;
		private int _disposed;

		// Pending connection queue; the client limit is enforced separately by MaxNumberOfClients
		private const int ListenBacklog = 10;

		// TCP keepalive so clients that drop without closing (power loss, cable pull) are detected in ~45s
		private const int KeepAliveTimeSeconds = 30;
		private const int KeepAliveIntervalSeconds = 5;
		private const int KeepAliveRetryCount = 3;

		private int _internalPort;
		private int _externalPort;


		private EiscApiAdvanced eisc;
		private GenericTcpServerBridgeJoinMap joinMap;

		private volatile bool _isListening;
		public bool IsListening
		{
			get { return _isListening; }
			private set
			{
				if (_isListening == value) return;

				_isListening = value;
				IsListeningFeedback.FireUpdate();
			}
		}
		public BoolFeedback IsListeningFeedback { get; private set; }
		public IntFeedback ClientsConnectedFeedback { get; private set; }

		/// <summary>
		/// TCP Server Device Constructor
		/// </summary>
		/// <param name="key"></param>
		/// <param name="name"></param>
		/// <param name="config"></param>
		public GenericTcpServer(string key, string name, GenericTcpServerPropertiesConfig config)
			: base(key, name)
		{
			_config = config;
			_internalPort = config.Port > 5000 ? config.Port : 5001;
			_externalPort = _internalPort;

			// keep track of connected clients
			_clients = new List<TcpClient>();

			// Initialize feedback instances once (not on every property access)
			// Created before the listener so the bridge can still link if listener setup fails
			IsListeningFeedback = new BoolFeedback("IsListeningFeedback", () => IsListening);
			ClientsConnectedFeedback = new IntFeedback("ClientsConnectedFeedback", () => { lock (_clientsLock) { return _clients.Count; } });

			try
			{
				_endPoint = new IPEndPoint(
				string.IsNullOrEmpty(_config.AddressToAcceptConnectionsFrom)
					? System.Net.IPAddress.Any
					: System.Net.IPAddress.Parse(_config.AddressToAcceptConnectionsFrom), _internalPort);

				_server = new TcpListener(_endPoint);
			}
			catch (Exception ex)
			{
				this.LogError(ex, "Exception in GenericTcpServer Constructor");
				return;
			}

			CrestronEnvironment.ProgramStatusEventHandler += HandleProgramEvent;
		}

		/// <summary>
		/// Device Initialize
		/// </summary>
		protected override void Initialize()
		{
			StartServer();

			if (IsListening)
			{
				AddPortForward();
			}
			base.Initialize();
		}

		/// <summary>
		/// Links the plugin device to the EISC bridge
		/// </summary>
		/// <param name="trilist"></param>
		/// <param name="joinStart"></param>
		/// <param name="joinMapKey"></param>
		/// <param name="bridge"></param>
		public override void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
		{
			joinMap = new GenericTcpServerBridgeJoinMap(joinStart);

			// This adds the join map to the collection on the bridge
			bridge?.AddJoinMap(Key, joinMap);

			var customJoins = JoinMapHelper.TryGetJoinMapAdvancedForDevice(joinMapKey);
			if (customJoins != null)
			{
				joinMap.SetCustomJoinData(customJoins);
			}

			eisc = bridge;

			this.LogInformation("LinkToApi: Linking to Trilist '{trilistId}'", trilist.ID.ToString("X"));
			this.LogInformation("LinkToApi: Linking to Bridge Type {bridgeType}", GetType().Name);

			// links to bridge
			trilist.SetString(joinMap.DeviceName.JoinNumber, Name);

			trilist.SetBoolSigAction(joinMap.IsListening.JoinNumber, b =>
			{
				this.LogInformation("LinkToApi: IsListening set to {status}", b ? "TRUE" : "FALSE");
				if (b)
					StartServer();
				else
					StopServer();
			});
			IsListeningFeedback.LinkInputSig(trilist.BooleanInput[joinMap.IsListening.JoinNumber]);
			ClientsConnectedFeedback.LinkInputSig(trilist.UShortInput[joinMap.ClientsConnected.JoinNumber]);

			trilist.SetSigTrueAction(joinMap.DisconnectAllClients.JoinNumber, DisconnectAllClients);

			trilist.SetStringSigAction(joinMap.DataSend.JoinNumber, SendTextToAllClients);

			trilist.OnlineStatusChange += (o, a) =>
			{
				if (!a.DeviceOnLine) return;

				trilist.SetString(joinMap.DeviceName.JoinNumber, Name);

				IsListeningFeedback.FireUpdate();
				ClientsConnectedFeedback.FireUpdate();
			};
		}

		/// <summary>
		/// Send text string to SIMPL bridge
		/// </summary>
		/// <param name="text"></param>
		public void SendTextToBridge(string text)
		{
			if (eisc == null)
			{
				this.LogError("SendTextToBridge: EISC is null, cannot send text");
				return;
			}

			if (text == null)
			{
				this.LogWarning("SendTextToBridge: text is null");
				return;
			}

			eisc.Eisc.SetString(joinMap.DataReceived.JoinNumber, text);
		}

		/// <summary>
		/// Send bytes to SIMPL bridge
		/// </summary>
		/// <param name="bytes"></param>
		public void SendBytesToBridge(byte[] bytes)
		{
			if (eisc == null)
			{
				this.LogError("SendBytesToBridge: EISC is null, cannot send bytes");
				return;
			}

			if (bytes == null || bytes.Length == 0)
			{
				this.LogWarning("SendBytesToBridge: bytes is null or empty");
				return;
			}

			var byteString = Encoding.ASCII.GetString(bytes, 0, bytes.Length);

			eisc.Eisc.SetString(joinMap.DataReceived.JoinNumber, byteString);
		}

		/// <summary>
		/// Send text to all connected clients
		/// </summary>
		/// <param name="text"></param>
		public void SendTextToAllClients(string text)
		{
			if (string.IsNullOrEmpty(text)) return;
			SendBytesToAllClients(Encoding.ASCII.GetBytes(text));
		}

		/// <summary>
		/// Send bytes to all connected clients
		/// </summary>
		/// <param name="bytes"></param>
		public void SendBytesToAllClients(byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0) return;
			TcpClient[] clientsSnapshot;
			lock (_clientsLock)
			{
				clientsSnapshot = _clients.ToArray();
			}
			foreach (var client in clientsSnapshot)
			{
				try
				{
					client.GetStream().Write(bytes, 0, bytes.Length);
				}
				catch (Exception ex)
				{
					// Closing the client ends its session thread, which removes it from the list
					this.LogWarning("SendBytesToAllClients: Write failed, closing client: {message}", ex.Message);
					client.Close();
				}
			}
		}

		/// <summary>
		/// Starts the TCP server listening for incoming client connections and starts the listener thread if not already running
		/// </summary>
		public void StartServer()
		{
			// Serializes start/stop between Initialize and the bridge IsListening join
			lock (_stateLock)
			{
				if (IsListening)
				{
					this.LogInformation("StartServer: TCP Server is listening");
					return;
				}

				if (_server == null)
				{
					this.LogError("StartServer: TCP listener was not created, check the device configuration");
					return;
				}

				try
				{
					_server.Start(ListenBacklog);
				}
				catch (System.Net.Sockets.SocketException ex)
				{
					this.LogError(ex, "StartServer: Unable to listen on {endPointAddress}:{endPointPort}", _endPoint.Address, _endPoint.Port);
					return;
				}

				IsListening = true;

				_listenerThread = new Thread(new ThreadStart(HandleClientConnections)) { IsBackground = true };
				_listenerThread.Start();

				this.LogInformation("StartServer: TCP Server has been started");
			}
		}

		/// <summary>
		/// Stops the TCP server from listening for new connections, disconnects all clients, and stops the listener thread
		/// </summary>
		public void StopServer()
		{
			lock (_stateLock)
			{
				if (!IsListening)
				{
					this.LogInformation("StopServer: TCP Server is stopped");
					return;
				}

				IsListening = false;
				_server.Stop();

				// Wait for the listener loop to exit so a quick restart can't race the old thread
				_listenerThread?.Join(1000);

				DisconnectAllClients();

				// commented out to prevent port forward removal on stop
				//RemovePortForward();

				this.LogInformation("StopServer: TCP Server has been stopped");
			}
		}

		/// <summary>
		/// Disconnects all connected clients and clears the client list
		/// </summary>
		public void DisconnectAllClients()
		{
			TcpClient[] clientsSnapshot;
			lock (_clientsLock)
			{
				clientsSnapshot = _clients.ToArray();
				_clients.Clear();
			}
			foreach (var client in clientsSnapshot)
			{
				try
				{
					client.Close();
				}
				catch (Exception ex)
				{
					this.LogError(ex, "DisconnectAllClients Exception");
				}
			}

			ClientsConnectedFeedback.FireUpdate();

			this.LogInformation("All clients disconnected");
		}

		private void AddPortForward()
		{
			try
			{
				this.LogInformation("AddPortForward: Automatically forwarding port {externalPort} to CSLAN", _externalPort);
				var csAdapterId = CrestronEthernetHelper.GetAdapterdIdForSpecifiedAdapterType(EthernetAdapterType.EthernetCSAdapter);
				var csIp = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_CURRENT_IP_ADDRESS, csAdapterId);

				var result = CrestronEthernetHelper.AddPortForwarding((ushort)_externalPort, (ushort)_internalPort, csIp, CrestronEthernetHelper.ePortMapTransport.TCP);

				if (result != CrestronEthernetHelper.PortForwardingUserPatRetCodes.NoErr)
				{
					this.LogError("AddPortForward: Error adding port forwarding: {error}\n\tNOTE: If port forward already exists, this is expected, showportmap to verify.", result);
				}
				else
				{
					this.LogInformation("AddPortForward: Successfully forwarded port {externalPort} to CSLAN", _externalPort);
				}
			}
			catch (ArgumentException)
			{
				this.LogInformation("AddPortForward: This processor does not have a CSLAN", this);
			}
			catch (Exception ex)
			{
				this.LogError(ex, "AddPortForward: Error automatically forwarding port to CSLAN");
			}
		}

		private void RemovePortForward()
		{
			try
			{
				this.LogInformation("RemovePortForward: Automatically removing port forwarding for port {externalPort} from CSLAN", _externalPort);
				var csAdapterId = CrestronEthernetHelper.GetAdapterdIdForSpecifiedAdapterType(EthernetAdapterType.EthernetCSAdapter);
				var csIp = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_CURRENT_IP_ADDRESS, csAdapterId);

				var result = CrestronEthernetHelper.RemovePortForwarding((ushort)_externalPort, (ushort)_internalPort, csIp, CrestronEthernetHelper.ePortMapTransport.TCP);

				if (result != CrestronEthernetHelper.PortForwardingUserPatRetCodes.NoErr)
				{
					this.LogError("RemovePortForward: Error removing port forwarding: {error}\n\tNOTE: If port forward does not exist, this is expected, showportmap to verify.", result);
				}
				else
				{
					this.LogInformation("RemovePortForward: Successfully removed port forwarding for port {externalPort} from CSLAN", _externalPort);
				}
			}
			catch (ArgumentException)
			{
				this.LogInformation("RemovePortForward: This processor does not have a CS LAN", this);
			}
			catch (Exception ex)
			{
				this.LogError(ex, "RemovePortForward: Error automatically removing port forwarding from CSLAN");
			}
		}

		void HandleProgramEvent(eProgramStatusEventType status)
		{
			if (status == eProgramStatusEventType.Stopping)
			{
				Dispose();
			}
		}

		void HandleClientConnections()
		{
			this.LogInformation("HandleClientConnections: TCP Server STARTED listening on {endPointAddress}:{endPointPort}", _endPoint.Address, _endPoint.Port);

			while (IsListening)
			{
				try
				{
					if (_server.Pending())
					{
						var client = _server.AcceptTcpClient();
						var remoteEndPoint = client.Client.RemoteEndPoint;

						bool accepted;
						lock (_clientsLock)
						{
							accepted = _config.MaxNumberOfClients <= 0 || _clients.Count < _config.MaxNumberOfClients;
							if (accepted)
							{
								_clients.Add(client);
							}
						}

						if (!accepted)
						{
							this.LogWarning("HandleClientConnections: Rejected client from {remoteEndPoint}, maximum of {maxNumberOfClients} clients reached", remoteEndPoint, _config.MaxNumberOfClients);
							client.Close();
							continue;
						}

						EnableKeepAlive(client);

						this.LogInformation("HandleClientConnections: Client connected from {remoteEndPoint}", remoteEndPoint);

						ClientsConnectedFeedback.FireUpdate();

						var t = new Thread(new ParameterizedThreadStart(HandleClientSession)) { IsBackground = true };
						t.Start(client);
					}
					else
					{
						Thread.Sleep(100); // Avoid busy waiting
					}
				}
				catch (Exception) when (!IsListening)
				{
					// Expected during shutdown when _server.Stop() interrupts Pending/AcceptTcpClient
					break;
				}
				catch (Exception ex)
				{
					this.LogError(ex, "HandleClientConnections Exception");
				}
			}

			this.LogInformation("HandleClientConnections: TCP Server STOPPED listening on {endPointAddress}:{endPointPort}", _endPoint.Address, _endPoint.Port);
		}

		void HandleClientSession(object obj)
		{
			var client = (TcpClient)obj;
			var remoteEndPoint = client.Client.RemoteEndPoint;
			try
			{
				using (var stream = client.GetStream())
				{
					var buffer = new byte[1024];
					int length;

					// Read blocks until data arrives and returns 0 when the client closes the connection
					while (IsListening && (length = stream.Read(buffer, 0, buffer.Length)) > 0)
					{
						var message = Encoding.ASCII.GetString(buffer, 0, length).Trim();

						SendTextToBridge(message);

						// Echo the message back to the client
						//var response = Encoding.ASCII.GetBytes($"Echo: {message}");
						//stream.Write(response, 0, response.Length);
					}
				}
			}
			catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
			{
				// Connection reset by the client, keepalive timeout, or closed locally (stop/disconnect/failed write)
			}
			catch (Exception ex)
			{
				this.LogError(ex, "HandleClientSession Exception");
			}
			finally
			{
				// Remove client from list when session ends
				lock (_clientsLock)
				{
					_clients.Remove(client);
				}
				client.Close();
				ClientsConnectedFeedback.FireUpdate();
				this.LogInformation("HandleClientSession: Client disconnected from {remoteEndPoint}", remoteEndPoint);
			}
		}

		void EnableKeepAlive(TcpClient client)
		{
			try
			{
				client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
				client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, KeepAliveTimeSeconds);
				client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, KeepAliveIntervalSeconds);
				client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, KeepAliveRetryCount);
			}
			catch (Exception ex)
			{
				this.LogWarning("EnableKeepAlive: Unable to enable TCP keepalive, dropped clients may not be detected: {message}", ex.Message);
			}
		}

		public void Dispose()
		{
			if (System.Threading.Interlocked.Exchange(ref _disposed, 1) == 1) return;

			CrestronEnvironment.ProgramStatusEventHandler -= HandleProgramEvent;

			if (_server != null)
			{
				StopServer();

				if (_server.Server != null)
				{
					_server.Server.Close();
					_server.Server.Dispose();
				}

				this.LogInformation("GenericTcpServer disposed");
			}
		}
	}
}