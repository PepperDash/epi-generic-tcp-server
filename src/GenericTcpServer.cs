// For Basic SIMPL# Classes
// For Basic SIMPL#Pro classes

using Crestron.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using IPEndPoint = System.Net.IPEndPoint;
using IPAddress =  System.Net.IPAddress;
using PepperDash.Core.Logging;

namespace PepperDash.Essentials.Plugins
{
    public class GenericTcpServer : EssentialsBridgeableDevice, IDisposable
    {
        private readonly GenericTcpServerConfig _config;
        private readonly IPEndPoint _endPoint;
        private readonly TcpListener _server;
        private readonly List<TcpClient> _clients;
        private Thread _listenerThread;

        private int _internalPort;
        private int _externalPort;


        private EiscApiAdvanced eisc;
        private GenericTcpServerBridgeJoinMap joinMap;

        private bool _isListening;
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
        public BoolFeedback IsListeningFeedback => new BoolFeedback("IsListeningFeedback", () => IsListening);

        
        public IntFeedback ClientsConnectedFeedback => new IntFeedback("ClientsConnectedFeedback", () => _clients.Count);

        /// <summary>
        /// TCP Server Device Constructor
        /// </summary>
        /// <param name="key"></param>
        /// <param name="name"></param>
        /// <param name="config"></param>
        public GenericTcpServer(string key, string name, GenericTcpServerConfig config)
            : base(key, name)
        {
            _config = config;
            _internalPort = config.Port > 5000 ? config.Port : 5001;
            _externalPort = _internalPort;

            try
            {
                _endPoint = new IPEndPoint(
                string.IsNullOrEmpty(_config.AddressToAcceptConnectionsFrom)
                    ? System.Net.IPAddress.Any
                    : System.Net.IPAddress.Parse(_config.AddressToAcceptConnectionsFrom), _internalPort);

                _server = new TcpListener(_endPoint);

                // keep track of connected clients
                _clients = new List<TcpClient>();
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
        public override void Initialize()
        {
            if (_server != null)
            {
                _server.Start(_config.MaxNumberOfClients);
                IsListening = true;

                // Start the listener thread AFTER the server has started
                _listenerThread = new Thread(new ThreadStart(HandleClientConnections));
                _listenerThread.Start();

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

            trilist.SetStringSigAction(joinMap.DataSend.JoinNumber, text =>
            {
                this.LogInformation("LinkToApi: '{text}' ==> SendTextTextToAllClients", text);
                SendTextToAllClients(text);
            });

            trilist.OnlineStatusChange += (o, a) =>
            {
                if (!a.DeviceOnLine) return;

                trilist.SetString(joinMap.DeviceName.JoinNumber, Name);

                IsListeningFeedback.FireUpdate();
                ClientsConnectedFeedback.FireUpdate();
            };
        }

        private void ComPortController_TextReceived(object sender, GenericCommMethodReceiveTextArgs e)
        {
            throw new NotImplementedException();
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

            this.LogWarning("SendTextToBridge: '{text}'", text);
            eisc.Eisc.SetString(joinMap.DataSend.JoinNumber, text);
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

            this.LogWarning("SendBytesToBridge: '{bytesString}'", byteString);
            eisc.Eisc.SetString(joinMap.DataSend.JoinNumber, byteString);
        }

        /// <summary>
        /// Send text to all connected clients
        /// </summary>
        /// <param name="text"></param>
        public void SendTextToAllClients(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var message = Encoding.ASCII.GetBytes(text);
            foreach (var client in _clients)
            {
                if (client.Connected)
                {
                    try
                    {
                        var stream = client.GetStream();
                        stream.Write(message, 0, message.Length);
                    }
                    catch (Exception ex)
                    {
                        this.LogError(ex, "SendTextToAllClients Exception");
                    }
                }
            }
        }

        /// <summary>
        /// Send bytes to all connected clients
        /// </summary>
        /// <param name="bytes"></param>
        public void SendBytesToAllClients(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            foreach (var client in _clients)
            {
                if (client.Connected)
                {
                    try
                    {
                        var stream = client.GetStream();
                        stream.Write(bytes, 0, bytes.Length);
                    }
                    catch (Exception ex)
                    {
                        this.LogError(ex, "SendBytesToAllClients Exception");
                    }
                }
            }
        }

        /// <summary>
        /// Starts the TCP server listening for incoming client connections and starts the listener thread if not already running
        /// </summary>
        public void StartServer()
        {
            if (IsListening)
            {
                this.LogInformation("StartServer: TCP Server {status}", IsListening ? "is listening" : "is stopped");
                return;
            }

            _server.Start(_config.MaxNumberOfClients);

            // IsListening = true;
            IsListening = _server.Server.IsBound;

            if (_listenerThread == null || !_listenerThread.IsAlive)
            {
                _listenerThread = new Thread(new ThreadStart(HandleClientConnections));
                _listenerThread.Start();
            }

            this.LogInformation("StartServer: TCP Server {status}", IsListening ? "has been started" : "is stopped");
        }

        /// <summary>
        /// Stops the TCP server from listening for new connections, disconnects all clients, and stops the listener thread
        /// </summary>
        public void StopServer()
        {
            if (!IsListening)
            {
                this.LogInformation("StopServer: TCP Server {status}", IsListening ? "is listening" : "is stopped");
                return;
            }

            DisconnectAllClients();

            _server.Stop();
            // IsListening = false;
            IsListening = _server.Server.IsBound;
            
            // commented out to prevent port forward removal on stop
            //RemovePortForward();

            this.LogInformation("StopServer: TCP Server {status}", IsListening ? "is listening" : "has been stopped");
        }

        /// <summary>
        /// Disconnects all connected clients and clears the client list
        /// </summary>
        public void DisconnectAllClients()
        {
            foreach (var client in _clients)
            {
                if (client.Connected)
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
            }
            _clients.Clear();

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
                    this.LogError("AddPortForward: Error adding port forwarding: {error}`n`tNOTE: If port forward already exists, this is expected, showportmap to verify.", result);
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
                    this.LogError("RemovePortForward: Error removing port forwarding: {error}`n`tNOTE: If port forward does not exist, this is expected, showportmap to verify.", result);
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
                        _clients.Add(client);
                        this.LogInformation("HandleClientConnections: Client connected from {remoteEndPoint}", client.Client.RemoteEndPoint);

                        ClientsConnectedFeedback.FireUpdate();

                        var t = new Thread(new ParameterizedThreadStart(HandleClientSession));
                        t.Start(client);
                    }
                    else
                    {
                        Thread.Sleep(100); // Avoid busy waiting
                    }
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
            try
            {
                var client = (TcpClient)obj;
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[1024];

                    while (IsListening && client.Connected)
                    {
                        try
                        {
                            if (stream.DataAvailable)
                            {
                                var length = stream.Read(buffer, 0, buffer.Length);

                                if (length > 0)
                                {
                                    var message = Encoding.ASCII.GetString(buffer, 0, length).Trim();
                                    this.LogVerbose("HandleClientSession: Received message from {remoteEndPoint}: {message}", client.Client.RemoteEndPoint, message);

                                    SendTextToBridge(message);

                                    // Echo the message back to the client
                                    //var response = Encoding.ASCII.GetBytes($"Echo: {message}");
                                    //stream.Write(response, 0, response.Length);
                                }
                            }
                            else
                            {
                                Thread.Sleep(10); // Small sleep when no data available
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // Stream was disposed during shutdown, exit gracefully
                            break;
                        }
                    }

                    ClientsConnectedFeedback.FireUpdate();
                }
            }
            catch (ObjectDisposedException)
            {
                // Expected during shutdown, no need to log as error
                this.LogDebug("HandleClientSession: Stream disposed during shutdown");
            }
            catch (Exception ex)
            {
                this.LogError(ex, "HandleClientSession Exception");
            }
        }

        public void Dispose()
        {
            if(_server != null)
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