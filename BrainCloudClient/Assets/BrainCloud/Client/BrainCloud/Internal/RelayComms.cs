//----------------------------------------------------
// brainCloud client source code
// Copyright 2016 bitHeads, inc.
//----------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using BrainCloud.JsonFx.Json;
using System.Net;
using System.Threading.Tasks;

namespace BrainCloud.Internal
{
    internal sealed class RelayComms
    {
        #region public consts
        public const int MAX_PACKETSIZE = 1024; // TODO:: based off of some config 

        public const byte MAX_PLAYERS = 128;
        public const byte INVALID_NET_ID = MAX_PLAYERS;
        public const byte RECV_CTRL_RSMG = INVALID_NET_ID + 1;                 // CONN_HEADER = "CONN" / RSMG
        public const byte RECV_CTRL_DNCT = INVALID_NET_ID + 2;                 // DNCT_HEADER = "DNCT"
        public const byte RECV_CTRL_RELAY = INVALID_NET_ID + 3;                // RLAY_HEADER = "RLAY"
        public const byte RECV_CTRL_PING = INVALID_NET_ID + 5;                 // PING_HEADER = "PING"
        public const byte RECV_CTRL_PONG = RECV_CTRL_PING;                     // PONG_HEADER = "PONG"
        public const byte RECV_CTRL_ACKN = INVALID_NET_ID + 6;                 // ACKN_HEADER = "ACKN"
        #endregion

        /// <summary>
        /// Last Synced Ping
        /// </summary>
        public long Ping { get; private set; }
        /// <summary>
        /// Specific Net Id of this User Connection
        /// </summary>
        public short NetId { get; private set; }

        /// <summary>
        /// Room Server Comms Constructor
        /// </summary>
        public RelayComms(BrainCloudClient in_client)
        {
            m_clientRef = in_client;
            NetId = -1;
        }

        /// <summary>
        /// Start off a connection, based off connection type to brainClouds Room Servers.  Connect options come in from "ROOM_ASSIGNED" lobby callback
        /// </summary>
        /// <param name="in_connectionType"></param>
        /// <param name="in_options">
        ///             connectionOptions["ssl"] = false;
        ///             connectionOptions["host"] = "168.0.1.192";
        ///             connectionOptions["port"] = 9000;
        ///             connectionOptions["passcode"] = ""somePasscode"
        ///             connectionOptions["lobbyId"] = "55555:v5v:001";
        ///</param>
        /// <param name="in_success"></param>
        /// <param name="in_failure"></param>
        /// <param name="cb_object"></param>
        public void Connect(RelayConnectionType in_connectionType = RelayConnectionType.WEBSOCKET, Dictionary<string, object> in_options = null, SuccessCallback in_success = null, FailureCallback in_failure = null, object cb_object = null)
        {
#if UNITY_WEBGL
            if (in_connectionType != RelayConnectionType.WEBSOCKET)
            {
                m_clientRef.Log("Non-WebSocket Connection Type Requested, on WEBGL.  Please connect via WebSocket.");

                if (in_failure != null)
                    in_failure(403, ReasonCodes.CLIENT_NETWORK_ERROR_TIMEOUT, buildRSRequestError("Non-WebSocket Connection Type Requested, on WEBGL.  Please connect via WebSocket."), cb_object);
                return;
            }
#endif
            Ping = 999;
            if (!m_bIsConnected)
            {
                // the callback
                m_connectedSuccessCallback = in_success;
                m_connectionFailureCallback = in_failure;
                m_connectedObj = cb_object;
                // read json
                //  --- ssl
                //  --- host
                //  --- port
                //  --- passcode
                //  --- lobbyId
                m_connectOptions = in_options;
                // connection type
                m_connectionType = in_connectionType;
                // now connect
                startReceivingRSConnectionAsync();
            }
        }

        /// <summary>
        /// Disables Real Time event for this session.
        /// </summary>
        public void Disconnect()
        {
            addRSCommandResponse(new RSCommandResponse(ServiceName.RoomServer.Value, "disconnect", "Disconnect Called"));
        }

        /// <summary>
        /// Register callback, so that data is received on the main thread
        /// </summary>
        ///
        public void RegisterDataCallback(RSDataCallback in_callback)
        {
            m_registeredDataCallback = in_callback;
        }

        /// <summary>
        /// Deregister the data callback
        /// </summary>
        public void DeregisterDataCallback()
        {
            m_registeredDataCallback = null;
        }

        /// <summary>
        /// send a string representation of data
        /// </summary>
        public void Send(string in_message)
        {
            send(in_message, RECV_CTRL_RELAY);
        }

        /// <summary>
        /// send byte array representation of data
        /// </summary>
        public void Send(byte[] in_data, byte in_header = RECV_CTRL_RELAY)
        {
            // appened RLAY to the beginning
            byte[] destination = appendReliableBytes(in_data, in_header);
            send(destination);
        }

        /// <summary>
        /// 
        /// </summary>
        public void SetPingInterval(float in_interval)
        {
            m_timeSinceLastPingRequest = 0;
            m_pingInterval = (int)(in_interval * 1000);
        }

        /// <summary>
        /// Callbacks responded to on the main thread
        /// </summary>
        public void Update()
        {
            RSCommandResponse toProcessResponse;
            lock (m_queuedRSCommands)
            {
                for (int i = 0; i < m_queuedRSCommands.Count; ++i)
                {
                    toProcessResponse = m_queuedRSCommands[i];

                    if (toProcessResponse.Operation == "connect" && m_bIsConnected && m_connectedSuccessCallback != null)
                    {
                        m_lastNowMS = DateTime.Now;
                        m_connectedSuccessCallback(toProcessResponse.JsonMessage, m_connectedObj);
                    }
                    else if (m_bIsConnected && (toProcessResponse.Operation == "error" || toProcessResponse.Operation == "disconnect"))
                    {
                        if (toProcessResponse.Operation == "disconnect")
                            disconnect();

                        // TODO:
                        if (m_connectionFailureCallback != null)
                            m_connectionFailureCallback(400, -1, toProcessResponse.JsonMessage, m_connectedObj);
                    }

                    if (!m_bIsConnected && toProcessResponse.Operation == "connect")
                    {
                        m_bIsConnected = true;
                        send(buildConnectionRequest());
                    }

                    if (m_registeredDataCallback != null && toProcessResponse.RawData != null)
                        m_registeredDataCallback(toProcessResponse.RawData);
                }

                m_queuedRSCommands.Clear();
            }

            if (m_bIsConnected)
            {
                DateTime nowMS = DateTime.Now;
                // the heart beat
                m_timeSinceLastPingRequest += (nowMS - m_lastNowMS).Milliseconds;
                m_lastNowMS = nowMS;

                if (m_timeSinceLastPingRequest >= m_pingInterval)
                {
                    m_timeSinceLastPingRequest = 0;
                    ping();
                }
            }
        }

        #region private
        /// <summary>
        /// 
        /// </summary>
        public void ping()
        {
            m_sentPing = DateTime.Now.Ticks;
            short lastPingShort = Convert.ToInt16(Ping * 0.0001);
            byte data1, data2;
            fromShortBE(lastPingShort, out data1, out data2);

            byte[] dataArr = { data1, data2 };
            Send(dataArr, RECV_CTRL_PING);
        }

        private byte[] buildConnectionRequest()
        {
            Dictionary<string, object> json = new Dictionary<string, object>();
#if !SMRJ_HACK
            json["profileId"] = m_clientRef.ProfileId;
#else
            json["profileId"] = "b09994cb-d91d-4060-876c-5430756ead7d";//  "841cf9fa-1a93-4a7a-a36b-e5833f7e239b"; //  
#endif
            json["lobbyId"] = m_connectOptions["lobbyId"] as string;
            json["passcode"] = m_connectOptions["passcode"] as string;

            byte[] array = concatenateByteArrays(CONNECT_ARR, Encoding.ASCII.GetBytes(JsonWriter.Serialize(json)));
            return array;
        }

        private string buildRSRequestError(string in_statusMessage)
        {
            Dictionary<string, object> json = new Dictionary<string, object>();
            json["status"] = 403;
            json["reason_code"] = ReasonCodes.RS_CLIENT_ERROR;
            json["status_message"] = in_statusMessage;
            json["severity"] = "ERROR";

            return JsonWriter.Serialize(json);
        }

        private byte[] buildDisconnectRequest()
        {
            return DISCONNECT_ARR;
        }


        /// <summary>
        /// 
        /// </summary>
        private void disconnect()
        {
            if (m_bIsConnected) send(buildDisconnectRequest());

            if (m_webSocket != null) m_webSocket.Close();
            m_webSocket = null;

            if (m_udpClient != null) m_udpClient.Close();
            m_udpClient = null;

            if (m_tcpClient != null) m_tcpClient.Close();
            m_tcpClient = null;

            m_bIsConnected = false;
            NetId = -1;
        }

        /// <summary>
        /// 
        /// </summary>
        private bool send(string in_message, byte in_header)
        {
            // appened in_header to the beginning
            byte[] data = Encoding.ASCII.GetBytes(in_message);
            byte[] destination = appendReliableBytes(data, in_header);
            return send(destination);
        }

        /// <summary>
        /// 
        /// </summary>
        private byte[] appendSizeBytes(byte[] in_data)
        {
            byte data1, data2;
            // size of data is the incoming data, plus the two that we're adding
            short sizeOfData = Convert.ToInt16(in_data.Length + SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY);
            fromShortBE(sizeOfData, out data1, out data2);
            // append length prefixed, before sending off
            byte[] dataArr = { data1, data2 };
            return concatenateByteArrays(dataArr, in_data);
        }

        /// <summary>
        /// 
        /// </summary>
        private byte[] appendReliableBytes(byte[] in_data, byte in_header = RECV_CTRL_RELAY)
        {
            byte[] destination;
            if (in_header == RECV_CTRL_RELAY)
            {
                byte[] header = { in_header, 0, 0 };
                destination = concatenateByteArrays(header, in_data);
            }
            else
            {

                byte[] header = { in_header };
                destination = concatenateByteArrays(header, in_data);
            }
            return destination;
        }

        /// <summary>
        /// raw send of byte[]
        /// </summary>
        private bool send(byte[] in_data)
        {
            bool bMessageSent = false;
            // early return, based on type
            switch (m_connectionType)
            {
                case RelayConnectionType.WEBSOCKET:
                    {
                        if (m_webSocket == null)
                            return bMessageSent;
                    }
                    break;
                case RelayConnectionType.TCP:
                    {
                        if (m_tcpClient == null)
                            return bMessageSent;
                    }
                    break;
                case RelayConnectionType.UDP:
                    {
                        if (m_udpClient == null)
                            return bMessageSent;
                    }
                    break;
                default: break;
            }
            // actually do the send
            try
            {
                //string recvOpp = Encoding.ASCII.GetString(in_data);
                //m_clientRef.Log(in_data.Length + "bytes RS " +  (m_connectionType == eRSConnectionType.WEBSOCKET ? "WS" : m_connectionType == eRSConnectionType.TCP ? "TCP" : "UDP") + " SEND msg : " + recvOpp);
                in_data = appendSizeBytes(in_data);
                switch (m_connectionType)
                {
                    case RelayConnectionType.WEBSOCKET:
                        {
                            m_webSocket.SendAsync(in_data);
                            bMessageSent = true;
                        }
                        break;
                    case RelayConnectionType.TCP:
                        {
                            tcpWrite(in_data);
                            bMessageSent = true;
                        }
                        break;
                    case RelayConnectionType.UDP:
                        {
                            m_udpClient.SendAsync(in_data, in_data.Length);
                            bMessageSent = true;
                        }
                        break;
                    default: break;
                }
            }
            catch (Exception socketException)
            {
                m_clientRef.Log("send exception: " + socketException);
                addRSCommandResponse(new RSCommandResponse(ServiceName.RoomServer.Value, "error", buildRSRequestError(socketException.ToString())));
            }

            return bMessageSent;
        }

        /// <summary>
        /// 
        /// </summary>
        private void startReceivingRSConnectionAsync()
        {
            bool sslEnabled = (bool)m_connectOptions["ssl"];
            string host = (string)m_connectOptions["host"];
            int port = (int)m_connectOptions["port"];
            switch (m_connectionType)
            {
                case RelayConnectionType.WEBSOCKET:
                    {
                        connectWebSocket(host, port, sslEnabled);
                    }
                    break;
                case RelayConnectionType.TCP:
                    {
                        connectTCPAsync(host, port);
                    }
                    break;
                case RelayConnectionType.UDP:
                    {
                        connectUDPAsync(host, port);
                    }
                    break;
                default: break;
            }
        }

        private void WebSocket_OnClose(BrainCloudWebSocket sender, int code, string reason)
        {
            m_clientRef.Log("Connection closed: " + reason);
            addRSCommandResponse(new RSCommandResponse(ServiceName.RoomServer.Value, "disconnect", reason));
        }

        private void Websocket_OnOpen(BrainCloudWebSocket accepted)
        {
            m_clientRef.Log("Connection established.");
            addRSCommandResponse(new RSCommandResponse(ServiceName.RoomServer.Value, "connect", ""));
        }

        private void WebSocket_OnMessage(BrainCloudWebSocket sender, byte[] data)
        {
            Array.Copy(data, SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY, data, 0, data.Length - SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY);
            onRecv(data, (data.Length - SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY) );
        }

        private void WebSocket_OnError(BrainCloudWebSocket sender, string message)
        {
            m_clientRef.Log("Error: " + message);
            addRSCommandResponse(new RSCommandResponse(ServiceName.RoomServer.Value, "error", buildRSRequestError(message)));
        }

        private bool isOppRSMG(byte controlByte)
        {
            return controlByte < MAX_PLAYERS || controlByte == RECV_CTRL_RSMG || controlByte == RECV_CTRL_RELAY;
        }
        /// <summary>
        /// 
        /// </summary>
        private void onRecv(byte[] in_data, int in_lengthOfData)
        {
            // assumed the length prefix is removed first
            if (in_data.Length >= CONTROL_BYTE_HEADER_LENGTH)
            {
                byte controlByte = in_data[0];// was an array now just one byte

                bool bOppRSMG = isOppRSMG(controlByte);
                int headerLength = CONTROL_BYTE_HEADER_LENGTH;
                if (bOppRSMG)
                    headerLength += SIZE_OF_RELIABLE_FLAGS;

                if (in_lengthOfData >= headerLength && 
                    bOppRSMG) // Room server msg or RLAY
                {
                    // bytes after the headers removed
                    byte[] cutOffData = new byte[in_lengthOfData - headerLength];
                    Buffer.BlockCopy(in_data, headerLength, cutOffData, 0, cutOffData.Length);

                    // do we need to parse the jsonMessage
                    string jsonMessage = NetId >= 0 ? "" : Encoding.ASCII.GetString(cutOffData);

                    // read in the netId if not set yet
                    if (jsonMessage != "")
                    {
                        Dictionary<string, object> parsedDict = (Dictionary<string, object>)JsonReader.Deserialize(jsonMessage);
                        if (parsedDict.ContainsKey("netId") && parsedDict.ContainsKey("profileId"))
                        {
                            if (parsedDict["profileId"] as string == m_clientRef.ProfileId)
                            {
                                NetId = Convert.ToInt16(parsedDict["netId"]);
                            }
                        }
                    }

                    //m_clientRef.Log("RS RECV: " + cutOffData.Length + "bytes - " + jsonMessage);
                    addRSCommandResponse(new RSCommandResponse(ServiceName.RoomServer.Value, "onrecv", jsonMessage, cutOffData));

                    // and acknowledge we got it
                    if (m_connectionType == RelayConnectionType.UDP)
                    {
                        cutOffData = new byte[SIZE_OF_RELIABLE_FLAGS];
                        Buffer.BlockCopy(in_data, CONTROL_BYTE_HEADER_LENGTH, cutOffData, 0, SIZE_OF_RELIABLE_FLAGS);
                        // send back ACKN
                        Send(cutOffData, RECV_CTRL_ACKN);
                    }
                }
                else if (controlByte == RECV_CTRL_PONG)
                {
                    Ping = DateTime.Now.Ticks - m_sentPing;
                    //m_clientRef.Log("LastPing: " + (LastPing * 0.0001f).ToString() + "ms");
                }
            }
        }

        private void onUDPRecv(IAsyncResult result)
        {
            // this is what had been passed into BeginReceive as the second parameter:
            UdpClient udpClient = result.AsyncState as UdpClient;

            string host = (string)m_connectOptions["host"];
            int port = (int)m_connectOptions["port"];
            IPEndPoint source = new IPEndPoint(IPAddress.Parse(host), port);

            if (udpClient != null)
            {
                // get the actual message and fill out the source:
                byte[] data = udpClient.EndReceive(result, ref source);
                Array.Copy(data, SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY, data, 0, data.Length - SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY);
                onRecv(data, (data.Length - SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY));

                //string in_message = Encoding.ASCII.GetString(data);
                //m_clientRef.Log("RS UDP AFTER RECV: " + (data.Length - SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY) + "bytes - " + in_message);

                // schedule the next receive operation once reading is done:
                udpClient.BeginReceive(new AsyncCallback(onUDPRecv), udpClient);
            }
        }

        /// <summary>
        /// Writes the specified message to the stream.
        /// </summary>
        /// <param name="message"></param>
        private void tcpWrite(byte[] message)
        {
            // Add this message to the list of message to send. If it's the only one in the
            // queue, fire up the async events to send it.
            try
            {
                lock (fLock)
                {
                    fToSend.Enqueue(message);
                    if (1 == fToSend.Count)
                    {
                        m_tcpStream.BeginWrite(message, 0, message.Length, tcpFinishWrite, null);
                    }

                }
            }
            catch (Exception e)
            {
                addRSCommandResponse(new RSCommandResponse(ServiceName.RTTRegistration.Value, "error", buildRSRequestError(e.ToString())));
            }
        }

        // ASync TCP Writes
        private object fLock = new object();
        private Queue<byte[]> fToSend = new Queue<byte[]>();
        private void tcpFinishWrite(IAsyncResult result)
        {
            try
            {
                m_tcpStream.EndWrite(result);
                lock (fLock)
                {
                    // Pop the message we just sent out of the queue
                    fToSend.Dequeue();

                    // See if there's anything else to send. Note, do not pop the message yet because
                    // that would indicate its safe to start writing a new message when its not.
                    if (fToSend.Count > 0)
                    {
                        byte[] final = fToSend.Peek();
                        m_tcpStream.BeginWrite(final, 0, final.Length, tcpFinishWrite, null);
                    }
                }
            }
            catch (Exception e)
            {
                addRSCommandResponse(new RSCommandResponse(ServiceName.RTTRegistration.Value, "error", buildRSRequestError(e.ToString())));
            }
        }

        // ASync TCP Reads
        const int SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY = 2;
        private int m_tcpBytesRead = 0; // the ones already processed
        private int m_tcpBytesToRead = 0; // the number to finish reading
        private byte[] m_tcpHeaderReadBuffer = new byte[SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY];
        private void onTCPReadHeader(IAsyncResult ar)
        {
            try
            {
                // Read precisely SIZE_OF_HEADER for the length of the following message
                int read = m_tcpStream.EndRead(ar);
                if (read == SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY)
                {
                    m_tcpBytesRead = 0;
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(m_tcpHeaderReadBuffer);

                    // from the header that was read, how much should we read after this until the next message ? 
                    m_tcpBytesToRead = BitConverter.ToInt16(m_tcpHeaderReadBuffer, 0);
                    m_tcpBytesToRead -= SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY;

                    m_tcpStream.BeginRead(m_tcpReadBuffer, 0, m_tcpBytesToRead, onTCPFinishRead, null);
                }
            }
            catch (Exception e)
            {
                addRSCommandResponse(new RSCommandResponse(ServiceName.RTTRegistration.Value, "error", buildRSRequestError(e.ToString())));
            }
        }
        private void onTCPFinishRead(IAsyncResult result)
        {
            try
            {
                // Finish reading from our stream. 0 bytes read means stream was closed
                int read = m_tcpStream.EndRead(result);
                if (0 == read)
                    throw new Exception();

                // Increment the number of bytes we've read. If there's still more to get, get them
                m_tcpBytesRead += read;
                if (m_tcpBytesRead < m_tcpBytesToRead)
                {
                    //m_clientRef.Log("m_tcpBytesRead < m_tcpBuffer.Length " + m_tcpBytesRead + " " + m_tcpBytesToRead);
                    m_tcpStream.BeginRead(m_tcpReadBuffer, m_tcpBytesRead, m_tcpBytesToRead - m_tcpBytesRead, onTCPFinishRead, null);
                    return;
                }

                // Should be exactly the right number read now.
                if (m_tcpBytesRead != m_tcpBytesToRead)
                {
                    throw new Exception();
                }

                //string in_message = Encoding.ASCII.GetString(m_tcpReadBuffer);
                //m_clientRef.Log("RS TCP RECV: " + m_tcpBytesToRead + "bytes - " + in_message);

                // Handle the message
                onRecv(m_tcpReadBuffer, m_tcpBytesToRead);
                // read the next header
                m_tcpBytesToRead = 0;
                m_tcpStream.BeginRead(m_tcpHeaderReadBuffer, 0, SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY, new AsyncCallback(onTCPReadHeader), null);
            }
            catch (Exception e)
            {
                addRSCommandResponse(new RSCommandResponse(ServiceName.RTTRegistration.Value, "error", buildRSRequestError(e.ToString())));
            }
        }

        private void connectWebSocket(string in_host, int in_port, bool in_sslEnabled)
        {
            string url = (in_sslEnabled ? "wss://" : "ws://") + in_host + ":" + in_port;
            m_webSocket = new BrainCloudWebSocket(url);
            m_webSocket.OnClose += WebSocket_OnClose;
            m_webSocket.OnOpen += Websocket_OnOpen;
            m_webSocket.OnMessage += WebSocket_OnMessage;
            m_webSocket.OnError += WebSocket_OnError;
        }

        private async void connectTCPAsync(string host, int port)
        {
            bool success = await Task.Run(async () =>
            {
                try
                {
                    m_tcpClient = new TcpClient();
                    m_tcpClient.NoDelay = true;
                    m_tcpClient.Client.NoDelay = true;
                    await m_tcpClient.ConnectAsync(host, port);
                }
                catch (Exception e)
                {
                    addRSCommandResponse(new RSCommandResponse(ServiceName.RoomServer.Value, "error", buildRSRequestError(e.ToString())));
                    return false;
                }
                return true;
            });

            if (success)
            {
                m_tcpStream = m_tcpClient.GetStream();
                addRSCommandResponse(new RSCommandResponse(ServiceName.RoomServer.Value, "connect", ""));
                m_tcpStream.BeginRead(m_tcpHeaderReadBuffer, 0, SIZE_OF_LENGTH_PREFIX_BYTE_ARRAY, new AsyncCallback(onTCPReadHeader), null);
            }
        }

        private void connectUDPAsync(string host, int port)
        {
            try
            {
                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.Completed += new EventHandler<SocketAsyncEventArgs>(OnUDPConnected);
                args.RemoteEndPoint = new DnsEndPoint(host, port);

                if (!m_udpClient.Client.ConnectAsync(args))
                {
                    OnUDPConnected(null, args);
                }
            }
            catch (Exception e)
            {
                addRSCommandResponse(new RSCommandResponse(ServiceName.RoomServer.Value, "error", buildRSRequestError(e.ToString())));
            }
        }

        private void OnUDPConnected(object sender, SocketAsyncEventArgs args)
        {
            addRSCommandResponse(new RSCommandResponse(ServiceName.RoomServer.Value, "connect", ""));
            m_udpClient.BeginReceive(new AsyncCallback(onUDPRecv), m_udpClient);
        }

        private void addRSCommandResponse(RSCommandResponse in_command)
        {
            lock (m_queuedRSCommands)
            {
                m_queuedRSCommands.Add(in_command);
            }
        }

        private byte[] concatenateByteArrays(byte[] a, byte[] b)
        {
            byte[] rv = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, rv, 0, a.Length);
            Buffer.BlockCopy(b, 0, rv, a.Length, b.Length);
            return rv;
        }

        private void fromShortBE(short number, out byte byte1, out byte byte2)
        {
            byte1 = (byte)(number >> 8);
            byte2 = (byte)(number >> 0);
        }

        private Dictionary<string, object> m_connectOptions = null;
        private RelayConnectionType m_connectionType = RelayConnectionType.INVALID;
        private bool m_bIsConnected = false;
        private DateTime m_lastNowMS;
        private int m_timeSinceLastPingRequest = 0;
        private int m_pingInterval = 1000; // one second

        // start
        // different connection types
        private BrainCloudWebSocket m_webSocket = null;
        private UdpClient m_udpClient = null;

        private TcpClient m_tcpClient = null;
        private NetworkStream m_tcpStream = null;
        private byte[] m_tcpReadBuffer = new byte[MAX_PACKETSIZE];
        // end 

        private const int CONTROL_BYTE_HEADER_LENGTH = 1;
        private const int SIZE_OF_RELIABLE_FLAGS = 2;

        private BrainCloudClient m_clientRef;
        private long m_sentPing = DateTime.Now.Ticks;
        private byte[] DISCONNECT_ARR = { RECV_CTRL_DNCT };
        private byte[] CONNECT_ARR = { RECV_CTRL_RSMG };

        // success callbacks
        private SuccessCallback m_connectedSuccessCallback = null;
        private FailureCallback m_connectionFailureCallback = null;
        private object m_connectedObj = null;

        private RSDataCallback m_registeredDataCallback = null;
        private List<RSCommandResponse> m_queuedRSCommands = new List<RSCommandResponse>();
        private struct RSCommandResponse
        {
            public RSCommandResponse(string in_service, string in_op, string in_msg, byte[] in_data = null)
            {
                Service = in_service;
                Operation = in_op;
                JsonMessage = in_msg;
                RawData = in_data;
            }
            public string Service { get; set; }
            public string Operation { get; set; }
            public string JsonMessage { get; set; }
            public byte[] RawData { get; set; }
        }
#endregion
    }
}

namespace BrainCloud
{
#region public enums
    public enum RelayConnectionType
    {
        INVALID,
        WEBSOCKET,
        TCP,
        UDP,

        MAX
    }
#endregion
}