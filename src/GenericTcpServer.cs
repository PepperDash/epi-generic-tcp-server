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

namespace PepperDash.Essentials.Plugins
{
    public class GenericTcpServer : EssentialsBridgeableDevice
    {
        private readonly GenericTcpServerConfig _config;
        private readonly IPEndPoint _endPoint;
        private readonly TcpListener _server;
        private readonly List<TcpClient> _clients;
        private Thread _listenerThread;

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

            try
            {
                _endPoint = new IPEndPoint(
                string.IsNullOrEmpty(_config.AddressToAcceptConnectionsFrom)
                    ? System.Net.IPAddress.Any
                    : System.Net.IPAddress.Parse(_config.AddressToAcceptConnectionsFrom),
                _config.Port > 5000 ? _config.Port : 5001);

                _server = new TcpListener(_endPoint);

                // keep track of connected clients
                _clients = new List<TcpClient>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"GenericTcpServer Constructor Exception: {ex}");
            }
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

            Debug.LogInformation("LinkToApi: Linking to Trilist '{0}'", trilist.ID.ToString("X"));
            Debug.LogInformation("LinkToApi: Linking to Bridge Type {0}", GetType().Name);

            // links to bridge
            trilist.SetString(joinMap.DeviceName.JoinNumber, Name);

            trilist.SetBoolSigAction(joinMap.IsListening.JoinNumber, b =>
            {
                Debug.LogInformation($"LinkToApi: IsListening set to {b}");
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
                Debug.LogInformation($"LinkToApi: '{text}' ==> SendTextTextToAllClients");
                SendTextToAllClients(text);
                //SendBytesToAllClients(Encoding.ASCII.GetBytes(text));
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
                Debug.LogError("SendTextToBridge: EISC is null, cannot send text");
                return;
            }

            if (text == null)
            {
                Debug.LogWarning("SendTextToBridge: text is null");
                return;
            }

            Debug.LogWarning($"SendTextToBridge: '{text}'");
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
                Debug.LogError("SendBytesToBridge: EISC is null, cannot send bytes");
                return;
            }

            if (bytes == null || bytes.Length == 0)
            {
                Debug.LogWarning("SendBytesToBridge: bytes is null or empty");
                return;
            }

            Debug.LogWarning($"SendBytesToBridge: '{Encoding.ASCII.GetString(bytes, 0, bytes.Length)}'");
            eisc.Eisc.SetString(joinMap.DataSend.JoinNumber, Encoding.ASCII.GetString(bytes, 0, bytes.Length));
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
                        Debug.LogError($"SendTextToAllClients Exception: {ex}");
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
                        Debug.LogError($"SendBytesToAllClients Exception: {ex}");
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
                Debug.LogInformation($"StartServer: TCP Server {(IsListening ? "is listening" : "is stopped")}");
                return;
            }

            _server.Start(_config.MaxNumberOfClients);
            IsListening = true;

            if (_listenerThread == null || !_listenerThread.IsAlive)
            {
                _listenerThread = new Thread(new ThreadStart(HandleClientConnections));
                _listenerThread.Start();
            }

            Debug.LogInformation($"StartServer: TCP Server {(IsListening ? "has been started" : "is stopped")}");
        }

        /// <summary>
        /// Stops the TCP server from listening for new connections, disconnects all clients, and stops the listener thread
        /// </summary>
        public void StopServer()
        {
            if (!IsListening)
            {
                Debug.LogInformation($"StopServer: TCP Server {(IsListening ? "is listening" : "is stopped")}");
                return;
            }

            IsListening = false;
            DisconnectAllClients();
            _server.Stop();

            Debug.LogInformation($"StopServer: TCP Server {(IsListening ? "is listening" : "has been stopped")}");
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
                        Debug.LogError($"DisconnectAllClients Exception: {ex}");
                    }
                }
            }
            _clients.Clear();

            ClientsConnectedFeedback.FireUpdate();

            Debug.LogInformation("All clients disconnected");
        }

        void HandleProgramEvent(eProgramStatusEventType status)
        {
            if (status == eProgramStatusEventType.Stopping)
            {
                IsListening = false;

                DisconnectAllClients();

                _server.Stop();
            }
        }

        void HandleClientConnections()
        {
            Debug.LogInformation($"HandleClientConnections: TCP Server STARTED listening on {_endPoint.Address}:{_endPoint.Port}");

            while (_isListening)
            {
                try
                {
                    if (_server.Pending())
                    {
                        var client = _server.AcceptTcpClient();
                        _clients.Add(client);
                        Debug.LogInformation($"HandleClientConnections: Client connected from {client.Client.RemoteEndPoint}");

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
                    Debug.LogError($"HandleClientConnections Exception: {ex}");
                }
            }

            Debug.LogInformation($"HandleClientConnections: TCP Server STOPPED listening on {_endPoint.Address}:{_endPoint.Port}");
        }

        void HandleClientSession(object obj)
        {
            try
            {
                var client = (TcpClient)obj;
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[1024];

                    while (client.Connected)
                    {
                        if (stream.DataAvailable)
                        {
                            var length = stream.Read(buffer, 0, buffer.Length);

                            if (length > 0)
                            {
                                var message = Encoding.ASCII.GetString(buffer, 0, length).Trim();
                                Debug.LogVerbose($"HandleClientSession: Received message from {client.Client.RemoteEndPoint}: {message}");

                                SendTextToBridge(message);

                                // Echo the message back to the client
                                //var response = Encoding.ASCII.GetBytes($"Echo: {message}");
                                //stream.Write(response, 0, response.Length);
                            }
                        }
                    }

                    ClientsConnectedFeedback.FireUpdate();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleClientSession Exception: {ex}");
            }
        }
    }
}