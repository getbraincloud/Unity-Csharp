//----------------------------------------------------
// brainCloud client source code
// Copyright 2016 bitHeads, inc.
//----------------------------------------------------

#if ((UNITY_5_3_OR_NEWER) && !UNITY_WEBPLAYER && (!UNITY_IOS || ENABLE_IL2CPP)) || UNITY_2018_3_OR_NEWER
#define USE_WEB_REQUEST //Comment out to force use of old WWW class on Unity 5.3+
#endif

namespace BrainCloud.Internal
{
    using System;
    using System.Collections.Generic;
    using System.Text;

#if (DOT_NET || DISABLE_SSL_CHECK)
using System.Net;
#endif
#if DOT_NET
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using BrainCloud.ModernHttpClient;
#else
#if USE_WEB_REQUEST
#if UNITY_5_3
using UnityEngine.Experimental.Networking;
#else
    using UnityEngine.Networking;
#endif
#endif
    using UnityEngine;
#endif

    using BrainCloud.JsonFx.Json;
    using System.IO;
    using System.IO.Compression;

    #region Processed Server Call Class
    public class ServerCallProcessed
    {
        internal ServerCall ServerCall { get; set; }
        public string Data { get; set; }
    }
    #endregion

    internal sealed class BrainCloudComms
    {
        /// <summary>
        ///Compress bundles sent from the client to the server for faster sending of large bundles.
        /// </summary>
        public bool SupportsCompression {get; private set;} = false;
        
        public void EnableCompression(bool compress)
        {
            SupportsCompression = compress;
        } 

        /// <summary>
        /// Byte size threshold that determines if the message size is something we want to compress or not. We make an initial value, but recevie the value for future calls based on the servers 
        ///auth response
        /// </summary>
        public int ClientSideCompressionThreshold{get; private set;} = 50000;

        /// <summary>
        /// The id of _expectedIncomingPacketId when no packet expected
        /// </summary>
        private static int NO_PACKET_EXPECTED = -1;

        /// <summary>
        /// Reference to the brainCloud client object
        /// </summary>
        private BrainCloudClient _clientRef;

        /// <summary>
        /// Set to true once Initialize has been called.
        /// </summary>
        private bool _initialized = false;

        /// <summary>
        /// Set to false if you want to shutdown processing on the Update.
        /// </summary>
        private bool _enabled = true;

        /// <summary>
        /// The next packet id to send
        /// </summary>
        private long _packetId = 0;

        /// <summary>
        /// The packet id we're expecting
        /// </summary>
        private long _expectedIncomingPacketId = NO_PACKET_EXPECTED;

        /// <summary>
        /// The service calls that are waiting to be sent.
        /// </summary>
        private List<ServerCall> _serviceCallsWaiting = new List<ServerCall>();

        /// <summary>
        /// The service calls that have been sent for which we are waiting for a reply
        /// </summary>
        private List<ServerCall> _serviceCallsInProgress = new List<ServerCall>();

        /// <summary>
        /// The service calls in the timeout queue.
        /// </summary>
        private List<ServerCall> _serviceCallsInTimeoutQueue = new List<ServerCall>();

        /// <summary>
        /// The current request state. Null if no request is in progress.
        /// </summary>
        private RequestState _activeRequest = null;

        /// <summary>
        /// The last time a packet was sent
        /// </summary>
        private DateTime _lastTimePacketSent;

        /// <summary>
        /// How long we wait to send a heartbeat if no packets have been sent or received.
        /// This value is set to a percentage of the heartbeat timeout sent by the authenticate response.
        /// </summary>
        private TimeSpan _idleTimeout = TimeSpan.FromSeconds(5 * 60);

        /// <summary>
        /// The maximum number of messages in a bundle.
        /// This is set to a value from the server on authenticate
        /// </summary>
        private int _maxBundleMessages = 10;

        /// <summary>
        /// The maximum number of sequential errors before client lockout
        /// This is set to a value from the server on authenticate
        /// </summary>
        private int _killSwitchThreshold = 11;

        ///<summary>
        ///The maximum number of attempts that the client can use
        ///while trying to successfully authenticate before the client 
        ///is disabled.
        ///<summary>
        private int _identicalFailedAuthAttemptThreshold = 3;

        ///<summary>
        ///The current number of identical failed attempts at authenticating. This 
        ///will reset when a successful authentication is made.
        ///<summary>
        private int _identicalFailedAuthenticationAttempts = 0;

        ///<summary>
        ///A blank reference for response data so we don't need to continually allocate new dictionaries when trying to
        ///make the data blank again.
        ///<summary>
        private Dictionary<string, object> blankResponseData = new Dictionary<string, object>();

        ///<summary>
        ///An array that stores the most recent response jsons as dictionaries.
        ///<summary>
        private Dictionary<string, object>[] _recentResponseJsonData = { new Dictionary<string, object>(), new Dictionary<string, object>() };

        /// <summary>
        /// When we have too many authentication errors under the same credentials, 
        /// the client will not be able to try and authenticate again until the timer is up.
        /// </summary>
        private TimeSpan _authenticationTimeoutDuration = TimeSpan.FromSeconds(30);

        /// <summary>
        /// When the authentication timer began 
        /// </summary>
        private DateTime _authenticationTimeoutStart;

        /// a checker to see what the packet Id we are receiving is 
        private long receivedPacketIdChecker = 0;

        /// <summary>
        /// Debug value to introduce packet loss for testing retries etc.
        /// </summary>
        //private double _debugPacketLossRate = 0;

        /// <summary>
        /// The event handler callback method
        /// </summary>
        private EventCallback _eventCallback;

        /// <summary>
        /// The reward handler callback method
        /// </summary>
        private RewardCallback _rewardCallback;

        private FileUploadSuccessCallback _fileUploadSuccessCallback;

        private FileUploadFailedCallback _fileUploadFailedCallback;

        private FailureCallback _globalErrorCallback;

        private NetworkErrorCallback _networkErrorCallback;

        private List<FileUploader> _fileUploads = new List<FileUploader>();

#if DOT_NET
        private HttpClient _httpClient = new HttpClient(new NativeMessageHandler());
#endif

        //For handling local session errors
        private int _cachedStatusCode;
        private int _cachedReasonCode;
        private string _cachedStatusMessage;

        //For kill switch
        private bool _killSwitchEngaged;
        private int _killSwitchErrorCount;
        private string _killSwitchService;
        private string _killSwitchOperation;

        private bool _isAuthenticated = false;

        public bool Authenticated
        {
            get
            {
                return _isAuthenticated;
            }
        }

        public long GetReceivedPacketId()
        {
            return receivedPacketIdChecker;
        }

        internal void setAuthenticated()
        {
            _isAuthenticated = true;
        }

        public Dictionary<string, string> AppIdSecretMap
        {
            get; private set;
        }

        public string AppId
        {
            get; private set;
        }

        public string SecretKey
        {
            get
            {
                if (AppIdSecretMap.ContainsKey(AppId))
                {
                    return AppIdSecretMap[AppId];
                }
                else
                {
                    return "NO SECRET DEFINED FOR '" + AppId + "'";
                }
            }
        }

        public string SessionID
        {
            get; private set;
        }
        internal void setSessionId(String sessionId)
        {
            SessionID = sessionId;
        }

        public string ServerURL
        {
            get; private set;
        }

        public string UploadURL
        {
            get; private set;
        }

        private int _uploadLowTransferRateTimeout = 120;
        public int UploadLowTransferRateTimeout
        {
            get { return _uploadLowTransferRateTimeout; }
            set { _uploadLowTransferRateTimeout = value; }
        }

        private int _uploadLowTransferRateThreshold = 50;
        public int UploadLowTransferRateThreshold
        {
            get { return _uploadLowTransferRateThreshold; }
            set { _uploadLowTransferRateThreshold = value; }
        }

        /// <summary>
        /// A list of packet timeouts. Index represents the packet attempt number.
        /// </summary>
        private List<int> _packetTimeouts = new List<int> { 15, 20, 35, 50 };
        public List<int> PacketTimeouts
        {
            get
            {
                return _packetTimeouts;
            }
            set
            {
                _packetTimeouts = value;
            }
        }
        public void SetPacketTimeoutsToDefault()
        {
            _packetTimeouts = new List<int> { 15, 20, 35, 50 };
        }

        private int _authPacketTimeoutSecs = 15;
        public int AuthenticationPacketTimeoutSecs
        {
            get
            {
                return _authPacketTimeoutSecs;
            }
            set
            {
                _authPacketTimeoutSecs = value;
            }
        }

        private bool _oldStyleStatusResponseInErrorCallback = false;
        public bool OldStyleStatusResponseInErrorCallback
        {
            get
            {
                return _oldStyleStatusResponseInErrorCallback;
            }
            set
            {
                _oldStyleStatusResponseInErrorCallback = value;
            }
        }

        private bool _cacheMessagesOnNetworkError = false;
        public void EnableNetworkErrorMessageCaching(bool enabled)
        {
            _cacheMessagesOnNetworkError = enabled;
        }

        /// <summary>
        /// This flag is set when _cacheMessagesOnNetworkError is true
        /// and a timeout occurs. It is reset when a call is made 
        /// to either RetryCachedMessages or FlushCachedMessages
        /// </summary>
        private bool _blockingQueue = false;

        public BrainCloudComms(BrainCloudClient client)
        {
#if DOT_NET
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
#endif
#if DISABLE_SSL_CHECK
            ServicePointManager.ServerCertificateValidationCallback = AcceptAllCertifications;
#endif
            AppIdSecretMap = new Dictionary<string, string>();
            _clientRef = client;
            ResetErrorCache();
        }


        /// <summary>
        /// Initialize the communications library with the specified serverURL and secretKey.
        /// </summary>
        /// <param name="serverURL">Server URL.</param>
        /// /// <param name="appId">AppId</param>
        /// <param name="secretKey">Secret key.</param>
        public void Initialize(string serverURL, string appId, string secretKey)
        {
            ResetCommunication(); //resets comms, packetId and SessionId
            _expectedIncomingPacketId = NO_PACKET_EXPECTED;

            ServerURL = serverURL;

            string suffix = @"/dispatcherv2";
            string formatURL = ServerURL.EndsWith(suffix) ? ServerURL.Substring(0, ServerURL.Length - suffix.Length) : ServerURL;
            
            //get rid of trailing / 
            while (formatURL.Length > 0 && formatURL.EndsWith("/"))
            {
                 formatURL = formatURL.Substring(0, formatURL.Length - 1);
            }

            UploadURL = formatURL;
            UploadURL += @"/uploader";

            AppIdSecretMap[appId] = secretKey;
            AppId = appId;

            _blockingQueue = false;
            _initialized = true;
        }

        /// <summary>
        /// Initialize the communications library with the specified serverURL and secretKey.
        /// </summary>
        /// <param name="serverURL">Server URL.</param>
        /// <param name="defaultAppId">default appId </param>
        /// /// <param name="appIdSecretMap">map of appId -> secrets, to allow the client to safely switch between apps with secret being secure</param>
        public void InitializeWithApps(string serverURL, string defaultAppId, Dictionary<string, string> appIdSecretMap)
        {
            AppIdSecretMap.Clear();
            AppIdSecretMap = appIdSecretMap;

            Initialize(serverURL, defaultAppId, AppIdSecretMap[defaultAppId]);
        }

        public void RegisterEventCallback(EventCallback cb)
        {
            _eventCallback = cb;
        }

        public void DeregisterEventCallback()
        {
            _eventCallback = null;
        }

        public void RegisterRewardCallback(RewardCallback cb)
        {
            _rewardCallback = cb;
        }

        public void DeregisterRewardCallback()
        {
            _rewardCallback = null;
        }

        public void RegisterFileUploadCallbacks(FileUploadSuccessCallback success, FileUploadFailedCallback failure)
        {
            _fileUploadSuccessCallback = success;
            _fileUploadFailedCallback = failure;
        }

        public void DeregisterFileUploadCallbacks()
        {
            _fileUploadSuccessCallback = null;
            _fileUploadFailedCallback = null;
        }

        public void RegisterGlobalErrorCallback(FailureCallback callback)
        {
            _globalErrorCallback = callback;
        }

        public void DeregisterGlobalErrorCallback()
        {
            _globalErrorCallback = null;
        }

        public void RegisterNetworkErrorCallback(NetworkErrorCallback callback)
        {
            _networkErrorCallback = callback;
        }

        public void DeregisterNetworkErrorCallback()
        {
            _networkErrorCallback = null;
        }

        /// <summary>
        /// The update method needs to be called periodically to send/receive responses
        /// and run the associated callbacks.
        /// </summary>
        public void Update()
        {
            // basic flow here is to:
            // 1- process existing requests
            // 2- send next request
            // 3- handle heartbeat/timeouts

            if (!_initialized)
            {
                return;
            }
            if (!_enabled)
            {
                return;
            }
            if (_blockingQueue)
            {
                return;
            }

            // process current request
            bool bypassTimeout = false;
            if (_activeRequest != null)
            {
                RequestState.eWebRequestStatus status = GetWebRequestStatus(_activeRequest);
                if (status == RequestState.eWebRequestStatus.STATUS_ERROR)
                {
                    // Force the timeout to be elapsed because we have completed the request with error
                    // or else, do nothing with the error right now - let the timeout code handle it
                    bypassTimeout = (_activeRequest.Retries >= GetMaxRetriesForPacket(_activeRequest));
                }
                else if (status == RequestState.eWebRequestStatus.STATUS_DONE)
                {
                    ResetIdleTimer();
                    HandleResponseBundle(GetWebRequestResponse(_activeRequest));

                    _activeRequest = null;
                }
            }

            // is it time for a retry?
            if (_activeRequest != null)
            {
                if (bypassTimeout || DateTime.Now.Subtract(_activeRequest.TimeSent) >= GetPacketTimeout(_activeRequest))
                {
                    // grab status/response before cancelling the request as in Unity, the www object
                    // will set internal status fields to null when www object is disposed
                    RequestState.eWebRequestStatus status = GetWebRequestStatus(_activeRequest);
                    string errorResponse = "";
                    if (status == RequestState.eWebRequestStatus.STATUS_ERROR)
                    {
                        errorResponse = GetWebRequestResponse(_activeRequest);
                    }
                    _activeRequest.CancelRequest();

                    if (!ResendMessage(_activeRequest))
                    {
                        // we've reached the retry limit - send timeout error to all client callbacks
                        if (_clientRef.LoggingEnabled)
                        {
                            if (status == RequestState.eWebRequestStatus.STATUS_ERROR)
                            {
                                _clientRef.Log("Timeout with network error: " + errorResponse);
                            }
                            else
                            {
                                _clientRef.Log("Timeout no reply from server");
                            }
                        }

                        _activeRequest = null;

                        // if we're doing caching of messages on timeout, kick it in now!
                        if (_cacheMessagesOnNetworkError && _networkErrorCallback != null)
                        {
                            if (_clientRef.LoggingEnabled)
                            {
                                _clientRef.Log("Caching messages");
                            }
                            _blockingQueue = true;

                            // and insert the inProgress messages into head of wait queue
                            lock (_serviceCallsInTimeoutQueue)
                            {
                                _serviceCallsInTimeoutQueue.InsertRange(0, _serviceCallsInProgress);
                                _serviceCallsInProgress.Clear();
                            }

                            _networkErrorCallback();
                        }
                        else
                        {
                            // Fake a message bundle to keep the callback logic in one place
                            TriggerCommsError(StatusCodes.CLIENT_NETWORK_ERROR, ReasonCodes.CLIENT_NETWORK_ERROR_TIMEOUT, "Timeout trying to reach brainCloud server");
                        }
                    }
                }
            }
            else // send the next message if we're ready
            {
                _activeRequest = CreateAndSendNextRequestBundle();
            }

            // is it time for a heartbeat?
            if (_isAuthenticated && !_blockingQueue)
            {
                if (DateTime.Now.Subtract(_lastTimePacketSent) >= _idleTimeout)
                {
                    SendHeartbeat();
                }
            }

            //if the client is currently locked on authentication calls. 
            if (tooManyAuthenticationAttempts())
            {
                if (_clientRef.LoggingEnabled)
                {
                    _clientRef.Log("TIMER ON");
                    _clientRef.Log(DateTime.Now.Subtract(_authenticationTimeoutStart).ToString());
                }
                //check the timeout, has enough time passed?
                if (DateTime.Now.Subtract(_authenticationTimeoutStart) >= _authenticationTimeoutDuration)
                {
                    if (_clientRef.LoggingEnabled)
                    {
                        _clientRef.Log("TIMER FINISHED");
                    }
                    //if the wait time is up they're free to make authentication calls again
                    _killSwitchEngaged = false;
                    ResetKillSwitch();
                }
            }

            RunFileUploadCallbacks();
        }

        #region File Upload

        /// <summary>
        /// Checks the status of active file uploads
        /// </summary>
        private void RunFileUploadCallbacks()
        {
            for (int i = _fileUploads.Count - 1; i >= 0; i--)
            {
                _fileUploads[i].Update();
                if (_fileUploads[i].Status == FileUploader.FileUploaderStatus.CompleteSuccess)
                {
                    if (_fileUploadSuccessCallback != null)
                    {
                        _fileUploadSuccessCallback(_fileUploads[i].UploadId, _fileUploads[i].Response);
                    }

                    if (_clientRef.LoggingEnabled)
                    {
                        _clientRef.Log("Upload success: " + _fileUploads[i].UploadId + " | " + _fileUploads[i].StatusCode + "\n" + _fileUploads[i].Response);
                    }
                    _fileUploads.RemoveAt(i);
                }
                else if (_fileUploads[i].Status == FileUploader.FileUploaderStatus.CompleteFailed)
                {
                    if (_fileUploadFailedCallback != null)
                    {
                        _fileUploadFailedCallback(_fileUploads[i].UploadId, _fileUploads[i].StatusCode, _fileUploads[i].ReasonCode, _fileUploads[i].Response);
                    }

                    if (_clientRef.LoggingEnabled)
                    {
                        _clientRef.Log("Upload failed: " + _fileUploads[i].UploadId + " | " + _fileUploads[i].StatusCode + "\n" + _fileUploads[i].Response);
                    }
                    _fileUploads.RemoveAt(i);
                }
            }
        }

        public void CancelUpload(string uploadFileId)
        {
            FileUploader uploader = GetFileUploader(uploadFileId);
            if (uploader != null) uploader.CancelUpload();
        }

        public double GetUploadProgress(string uploadFileId)
        {
            FileUploader uploader = GetFileUploader(uploadFileId);
            if (uploader != null) return uploader.Progress;
            else return -1;
        }

        public long GetUploadBytesTransferred(string uploadFileId)
        {
            FileUploader uploader = GetFileUploader(uploadFileId);
            if (uploader != null) return uploader.BytesTransferred;
            else return -1;
        }

        public long GetUploadTotalBytesToTransfer(string uploadFileId)
        {
            FileUploader uploader = GetFileUploader(uploadFileId);
            if (uploader != null) return uploader.TotalBytesToTransfer;
            else return -1;
        }

        private FileUploader GetFileUploader(string uploadId)
        {
            for (int i = 0; i < _fileUploads.Count; i++)
            {
                if (_fileUploads[i].UploadId == uploadId) return _fileUploads[i];
            }
            
            if (_clientRef.LoggingEnabled)
            {
                _clientRef.Log("GetUploadProgress could not find upload ID " + uploadId);
            }
            return null;
        }

        #endregion

        /// <summary>
        /// Method fakes a json error from the server and sends
        /// it along to the response callbacks.
        /// </summary>
        /// <param name="status">status.</param>
        /// <param name="reasonCode">reason code.</param>
        /// <param name="statusMessage">status message.</param>
        private void TriggerCommsError(int status, int reasonCode, string statusMessage)
        {
            // error json format is
            // {
            // "reason_code": 40316,
            // "status": 403,
            // "status_message": "Processing exception: Invalid game ID in authentication request",
            // "severity": "ERROR"
            // }

            int numMessagesToReturn = 0;
            lock (_serviceCallsInProgress)
            {
                numMessagesToReturn = _serviceCallsInProgress.Count;
            }
            if (numMessagesToReturn <= 0)
            {
                numMessagesToReturn = 1; // for when we want to send to only global error callback
            }

            JsonResponseErrorBundleV2 bundleObj = new JsonResponseErrorBundleV2();
            bundleObj.packetId = _expectedIncomingPacketId;
            bundleObj.responses = new JsonErrorMessage[numMessagesToReturn];
            for (int i = 0; i < numMessagesToReturn; ++i)
            {
                bundleObj.responses[i] = new JsonErrorMessage(status, reasonCode, statusMessage);
            }
            string jsonError = JsonWriter.Serialize(bundleObj);
            HandleResponseBundle(jsonError);
        }

        /// <summary>
        /// Shuts down the communications layer.
        /// Make sure to only call this from the main thread!
        /// </summary>
        public void ShutDown()
        {
            lock (_serviceCallsWaiting)
            {
                _serviceCallsWaiting.Clear();
            }

            // force a log out
            ServerCallback callback = BrainCloudClient.CreateServerCallback(null, null, null);
            ServerCall sc = new ServerCall(ServiceName.PlayerState, ServiceOperation.Logout, null, callback);
            AddToQueue(sc);

            _activeRequest = null;

            // calling update will try to send the logout
            Update();

            // and then dump the comms layer
            ResetCommunication();
        }

        // see BrainCloudClient.RetryCachedMessages() docs
        public void RetryCachedMessages()
        {
            if (_blockingQueue)
            {
                if (_clientRef.LoggingEnabled)
                {
                    _clientRef.Log("Retrying cached messages");
                }

                if (_activeRequest != null)
                {
                    // this is definitely an error in the comms lib if it happens. 
                    // we attempt to cancel it but this is uncharted territory.

                    if (_clientRef.LoggingEnabled)
                    {
                        _clientRef.Log("ERROR - retrying cached messages but there is an active request!");
                    }
                    _activeRequest.CancelRequest();
                    _activeRequest = null;
                }

                --_packetId;
                _activeRequest = CreateAndSendNextRequestBundle();
                _blockingQueue = false;
            }
        }

        // see BrainCloudClient.FlushCachedMessages() docs
        public void FlushCachedMessages(bool sendApiErrorCallbacks)
        {
            if (_blockingQueue)
            {
                if (_clientRef.LoggingEnabled)
                {
                    _clientRef.Log("Flushing cached messages");
                }

                // try to cancel if request is in progress (shouldn't happen)
                if (_activeRequest != null)
                {
                    _activeRequest.CancelRequest();
                    _activeRequest = null;
                }

                // then flush the message queues
                List<ServerCall> callsToProcess = new List<ServerCall>();
                lock (_serviceCallsInTimeoutQueue)
                {
                    for (int i = 0, isize = _serviceCallsInTimeoutQueue.Count; i < isize; ++i)
                    {
                        callsToProcess.Add(_serviceCallsInTimeoutQueue[i]);
                    }
                    _serviceCallsInTimeoutQueue.Clear();
                }
                lock (_serviceCallsWaiting)
                {
                    for (int i = 0, isize = _serviceCallsWaiting.Count; i < isize; ++i)
                    {
                        callsToProcess.Add(_serviceCallsWaiting[i]);
                    }
                    _serviceCallsWaiting.Clear();
                }
                lock (_serviceCallsInProgress)
                {
                    _serviceCallsInProgress.Clear(); // shouldn't be anything in here...
                }

                // and send api error callbacks if required
                if (sendApiErrorCallbacks)
                {
                    for (int i = 0, isize = callsToProcess.Count; i < isize; ++i)
                    {
                        ServerCall sc = callsToProcess[i];
                        if (sc.GetCallback() != null)
                        {
                            sc.GetCallback().OnErrorCallback(
                                StatusCodes.CLIENT_NETWORK_ERROR,
                                ReasonCodes.CLIENT_NETWORK_ERROR_TIMEOUT,
                                "Timeout trying to reach brainCloud server");
                        }
                    }
                }
                _blockingQueue = false;
            }
        }


        internal void InsertEndOfMessageBundleMarker()
        {
            this.AddToQueue(new EndOfBundleMarker());
        }

        /// <summary>
        /// Resets the idle timer.
        /// </summary>
        private void ResetIdleTimer()
        {
            _lastTimePacketSent = DateTime.Now;
        }

        /// <summary>
        /// Starts timeout of authentication calls.
        /// </summary>
        private void ResetAuthenticationTimer()
        {
            _authenticationTimeoutStart = DateTime.Now;
        }

        ///<summary>
        ///keeps track of if the client has made too many authentication attempts.
        ///<summary>
        private bool tooManyAuthenticationAttempts()
        {
            return _identicalFailedAuthenticationAttempts >= _identicalFailedAuthAttemptThreshold;
        }

        //save profileid and sessionId of response
        void SaveProfileAndSessionIds(Dictionary<string, object> responseData, string data)
        {
            // save the session ID
            string sessionId = GetJsonString(responseData, OperationParam.ServiceMessageSessionId.Value, null);
            if (sessionId != null)
            {
                SessionID = sessionId;
                _isAuthenticated = true;
            }

            // save the profile Id
            string profileId = GetJsonString(responseData, OperationParam.ProfileId.Value, null);
            if (profileId != null)
            {
                _clientRef.AuthenticationService.ProfileId = profileId;
            }
        }

        /// <summary>
        /// Handles the response bundle and calls registered callbacks.
        /// </summary>
        /// <param name="jsonData">The received message bundle.</param>
        private void HandleResponseBundle(string jsonData)
        {
            if (_clientRef.LoggingEnabled)
            {
                _clientRef.Log(String.Format("{0} - {1}\n{2}", "RESPONSE", DateTime.Now, jsonData));
            }

            if (string.IsNullOrEmpty(jsonData))
            {
                if (_clientRef.LoggingEnabled)
                {
                    _clientRef.Log("ERROR - Incoming packet data was null or empty! This is probably a network issue.");
                }
                return;
            }

            JsonResponseBundleV2 bundleObj = JsonReader.Deserialize<JsonResponseBundleV2>(jsonData);
            Dictionary<string, object>[] responseBundle = bundleObj.responses;
            Dictionary<string, object> response = null;
            long receivedPacketId = (long)bundleObj.packetId;
            receivedPacketIdChecker = receivedPacketId;

            // if the receivedPacketId is NO_PACKET_EXPECTED (-1), its a serious error, which cannot be retried
            // errors for whcih NO_PACKET_EXPECTED are:
            // json parsing error, missing packet id, app secret changed via the portal
            if (receivedPacketId != NO_PACKET_EXPECTED && (_expectedIncomingPacketId == NO_PACKET_EXPECTED || _expectedIncomingPacketId != receivedPacketId))
            {
                if (_clientRef.LoggingEnabled)
                {
                    _clientRef.Log("Dropping duplicate packet");
                }

                for (int j = 0; j < responseBundle.Length; ++j)
                {
                    lock (_serviceCallsInProgress)
                    {
                        if (_serviceCallsInProgress.Count > 0)
                        {
                            _serviceCallsInProgress.RemoveAt(0);
                        }
                    }
                }
                return;
            }
            
            _expectedIncomingPacketId = NO_PACKET_EXPECTED;
            IList<Exception> exceptions = new List<Exception>();

            string data = "";
            ServerCall sc = null;
            ServerCallback callback = null;
            string service = "";
            string operation = "";
            Dictionary<string, object> responseData = null;
            for (int j = 0; j < responseBundle.Length; ++j)
            {
                response = responseBundle[j];
                int statusCode = (int)response["status"];
                data = "";
                responseData = null;
                sc = null;
                callback = null;
                service = "";
                operation = "";
                //
                // It's important to note here that a user error callback *might* call
                // ResetCommunications() based on the error being returned.
                // ResetCommunications will clear the _serviceCallsInProgress List
                // effectively removing all registered callbacks for this message bundle.
                // It's also likely that the developer will want to call authenticate next.
                // We need to ensure that this is supported as it's the best way to 
                // reset the brainCloud communications after a session invalid or network
                // error is triggered.
                //
                // This is safe to do from the main thread but just in case someone
                // calls this method from another thread, we lock on _serviceCallsWaiting
                //
                lock (_serviceCallsWaiting)
                {
                    if (_serviceCallsInProgress.Count > 0)
                    {
                        sc = _serviceCallsInProgress[0] as ServerCall;
                        _serviceCallsInProgress.RemoveAt(0);
                    }
                }

                // its a success response
                if (statusCode == 200)
                {
                    ResetKillSwitch();
                    service = sc.GetService();
                    if (response[OperationParam.ServiceMessageData.Value] != null)
                    {
                        responseData = (Dictionary<string, object>)response[OperationParam.ServiceMessageData.Value];
                        // send the data back as not formatted
                        data = JsonWriter.Serialize(response);

                        if (service == ServiceName.Authenticate.Value || service == ServiceName.Identity.Value)
                        {
                            SaveProfileAndSessionIds(responseData, data);
                        }
                    }
                    else
                    {
                        data = JsonWriter.Serialize(response);
                    }

                    // now try to execute the callback
                    if (sc != null)
                    {
                        callback = sc.GetCallback();
                        operation = sc.GetOperation();
                        bool bIsPeerScriptUploadCall = false;
                        try
                        {
                            bIsPeerScriptUploadCall = operation == ServiceOperation.RunPeerScript.Value &&
                                                     response.ContainsKey(OperationParam.ServiceMessageData.Value) &&
                                                     ((Dictionary<string, object>)response[OperationParam.ServiceMessageData.Value]).ContainsKey("response") &&
                                                     ((Dictionary<string, object>)((Dictionary<string, object>)response[OperationParam.ServiceMessageData.Value])["response"]).ContainsKey(OperationParam.ServiceMessageData.Value) &&
                                                     ((Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)response[OperationParam.ServiceMessageData.Value])["response"])[OperationParam.ServiceMessageData.Value]).ContainsKey("fileDetails");
                        }
                        catch (Exception) { }

                        if (operation == ServiceOperation.FullReset.Value ||
                            operation == ServiceOperation.Logout.Value)
                        {
                            // we reset the current player or logged out
                            // we are no longer authenticated
                            _isAuthenticated = false;
                            SessionID = "";
                            _clientRef.AuthenticationService.ClearSavedProfileID();
                            ResetErrorCache();
                        }
                        //either off of authenticate or identity call, be sure to save the profileId and sessionId
                        else if (operation == ServiceOperation.Authenticate.Value)
                        {
                            ProcessAuthenticate(responseData);
                        }
                        // switch to child
                        else if (operation.Equals(ServiceOperation.SwitchToChildProfile.Value) ||
                            operation.Equals(ServiceOperation.SwitchToParentProfile.Value))
                        {
                            ProcessSwitchResponse(responseData);
                        }
                        else if (operation == ServiceOperation.PrepareUserUpload.Value || bIsPeerScriptUploadCall)
                        {
                            string peerCode = bIsPeerScriptUploadCall && sc.GetJsonData().Contains("peer") ? (string)sc.GetJsonData()["peer"] : "";
                            var fileData = peerCode == "" ? (Dictionary<string, object>)responseData["fileDetails"] :
                                (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)responseData["response"])[OperationParam.ServiceMessageData.Value])["fileDetails"];

                            if (fileData.ContainsKey("uploadId") && fileData.ContainsKey("localPath"))
                            {
                                string uploadId = (string)fileData["uploadId"];
                                string localPath = (string)fileData["localPath"];
                                string fileName = (string) fileData["cloudFilename"];
                                var uploader = new FileUploader(uploadId, localPath, UploadURL, SessionID,
                                    _uploadLowTransferRateTimeout, _uploadLowTransferRateThreshold, _clientRef, peerCode);
                                
                                uploader.FileName = fileName;
                                if (_clientRef.FileService.FileStorage.ContainsKey(localPath))
                                {
                                    uploader.TotalBytesToTransfer = _clientRef.FileService.FileStorage[localPath].Length;    
                                }
#if DOT_NET                     
                                uploader.HttpClient = _httpClient;
#endif
                                _fileUploads.Add(uploader);
                                uploader.Start();
                            }
                        }

                        // // only process callbacks that are real
                        if (callback != null)
                        {
                            try
                            {
                                callback.OnSuccessCallback(data);
                            }
                            catch (Exception e)
                            {
                                if (_clientRef.LoggingEnabled)
                                {
                                    _clientRef.Log(e.StackTrace);
                                }
                                exceptions.Add(e);
                            }
                        }

                        // now deal with rewards
                        if (_rewardCallback != null && responseData != null)
                        {
                            try
                            {
                                Dictionary<string, object> rewards = null;

                                // it's an operation that return a reward
                                if (operation == ServiceOperation.Authenticate.Value)
                                {
                                    object objRewards = null;
                                    if (responseData.TryGetValue("rewards", out objRewards))
                                    {
                                        Dictionary<string, object> outerRewards = (Dictionary<string, object>)objRewards;
                                        if (outerRewards.TryGetValue("rewards", out objRewards))
                                        {
                                            Dictionary<string, object> innerRewards = (Dictionary<string, object>)objRewards;
                                            if (innerRewards.Count > 0)
                                            {
                                                // we found rewards
                                                rewards = outerRewards;
                                            }
                                        }
                                    }
                                }
                                else if (operation == ServiceOperation.Update.Value ||
                                    operation == ServiceOperation.Trigger.Value ||
                                    operation == ServiceOperation.TriggerMultiple.Value)
                                {
                                    object objRewards = null;
                                    if (responseData.TryGetValue("rewards", out objRewards))
                                    {
                                        Dictionary<string, object> innerRewards = (Dictionary<string, object>)objRewards;
                                        if (innerRewards.Count > 0)
                                        {
                                            // we found rewards
                                            rewards = responseData;
                                        }
                                    }
                                }

                                if (rewards != null)
                                {
                                    Dictionary<string, object> theReward = new Dictionary<string, object>();
                                    theReward["rewards"] = rewards;
                                    theReward["service"] = service;
                                    theReward["operation"] = operation;
                                    Dictionary<string, object> apiRewards = new Dictionary<string, object>();
                                    List<object> rewardList = new List<object>();
                                    rewardList.Add(theReward);
                                    apiRewards["apiRewards"] = rewardList;

                                    string rewardsAsJson = JsonWriter.Serialize(apiRewards);

                                    _rewardCallback(rewardsAsJson);
                                }
                            }
                            catch (Exception e)
                            {
                                if (_clientRef.LoggingEnabled)
                                {
                                    _clientRef.Log(e.StackTrace);
                                }
                                exceptions.Add(e);
                            }
                        }
                    }
                }
                else //if non-200
                {
                    object reasonCodeObj = null, statusMessageObj = null;
                    int reasonCode = 0;
                    string errorJson = "";
                    callback = sc.GetCallback();
                    operation = sc.GetOperation();

                    //if it was an authentication call 
                    if (operation == ServiceOperation.Authenticate.Value)
                    {
                        //swap the recent responses, so you have the newest one, and the one last time you came through.
                        _recentResponseJsonData[1] = _recentResponseJsonData[0];
                        _recentResponseJsonData[0] = response;

                        //need to compare the json data of the most recent response and the last response. If they are the same, it means the client
                        //is attempting the exact same authentication call. 
                        bool responsesAreTheSame = true;
                        //if the data has different lengths, they're obviously not the same
                        if (_recentResponseJsonData[0].Count == _recentResponseJsonData[1].Count)
                        {
                            foreach (var pair in _recentResponseJsonData[0])
                            {
                                object value = null;
                                //if there is ever a time they're not the same value, then they are not the same
                                if (_recentResponseJsonData[1].TryGetValue(pair.Key, out value))
                                {
                                    //if the values are not the same theyre different
                                    if (value.ToString() != pair.Value.ToString())
                                    {
                                        responsesAreTheSame = false;
                                        break;
                                    }
                                }
                                else
                                {
                                    //If the key isnt found, they also can't be the same
                                    responsesAreTheSame = false;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            //different lengths of data mean theyre not the same call.
                            responsesAreTheSame = false;
                        }

                        //if we haven't already gone above the threshold and are waiting for the timer or a 200 response to reset things
                        if (!tooManyAuthenticationAttempts())
                        {
                            //we either increment the amount of identical failed authentication attempts, or reset it because its not identical. 
                            if (responsesAreTheSame == true)
                            {
                                _identicalFailedAuthenticationAttempts++;
                            }
                            else
                            {
                                _identicalFailedAuthenticationAttempts = 0;
                            }
                        }
                    }

                    if (response.TryGetValue("reason_code", out reasonCodeObj))
                    {
                        reasonCode = (int)reasonCodeObj;
                    }

                    if (_oldStyleStatusResponseInErrorCallback)
                    {
                        if (response.TryGetValue("status_message", out statusMessageObj))
                        {
                            errorJson = (string)statusMessageObj;
                        }
                    }
                    else
                    {
                        errorJson = JsonWriter.Serialize(response);
                    }

                    if (reasonCode == ReasonCodes.PLAYER_SESSION_EXPIRED
                        || reasonCode == ReasonCodes.NO_SESSION
                        || reasonCode == ReasonCodes.PLAYER_SESSION_LOGGED_OUT)
                    {
                        _isAuthenticated = false;
                        SessionID = "";
                        
                        if (_clientRef.LoggingEnabled)
                        {
                            _clientRef.Log("Received session expired or not found, need to re-authenticate");
                        }

                        // cache error if session related
                        _cachedStatusCode = statusCode;
                        _cachedReasonCode = reasonCode;

                        object status = null;
                        if (response.TryGetValue("status_message", out status))
                        {
                            _cachedStatusMessage = status as string;
                        }
                    }

                    if (operation == ServiceOperation.Logout.Value)
                    {
                        if (reasonCode == ReasonCodes.CLIENT_NETWORK_ERROR_TIMEOUT)
                        {
                            _isAuthenticated = false;
                            SessionID = "";
                            if (_clientRef.LoggingEnabled)
                            {
                                _clientRef.Log("Could not communicate with the server on logout due to network timeout");
                            }
                        }
                    }

                    // now try to execute the callback
                    if (callback != null)
                    {
                        try
                        {
                            callback.OnErrorCallback(statusCode, reasonCode, errorJson);
                        }
                        catch (Exception e)
                        {
                            if (_clientRef.LoggingEnabled)
                            {
                                _clientRef.Log(e.StackTrace);
                            }
                            exceptions.Add(e);
                        }
                    }

                    if (_globalErrorCallback != null)
                    {
                        object cbObject = null;
                        if (callback != null)
                        {
                            cbObject = callback.m_cbObject;
                            // if this is the internal BrainCloudWrapper callback object return the user-supplied
                            // callback object instead
                            if (cbObject != null && cbObject is WrapperAuthCallbackObject)
                            {
                                cbObject = ((WrapperAuthCallbackObject)cbObject)._cbObject;
                            }
                        }

                        _globalErrorCallback(statusCode, reasonCode, errorJson, cbObject);
                    }

                    UpdateKillSwitch(sc.Service, sc.Operation, statusCode);
                }
            }

            if (bundleObj.events != null && _eventCallback != null)
            {
                Dictionary<string, Dictionary<string, object>[]> eventsJsonObj = new Dictionary<string, Dictionary<string, object>[]>();
                eventsJsonObj["events"] = bundleObj.events;
                string eventsAsJson = JsonWriter.Serialize(eventsJsonObj);
                try
                {
                    _eventCallback(eventsAsJson);
                }
                catch (Exception e)
                {
                    if (_clientRef.LoggingEnabled)
                    {
                        _clientRef.Log(e.StackTrace);
                    }
                    exceptions.Add(e);
                }
            }

            if (exceptions.Count > 0)
            {
                _activeRequest = null; // to make sure we don't reprocess this message

                throw new Exception("User callback handlers threw " + exceptions.Count + " exception(s)."
                                    + " See the Unity log for callstacks or inner exception for first exception thrown.",
                                    exceptions[0]);
            }
        }

        private void UpdateKillSwitch(string service, string operation, int statusCode)
        {
            if (statusCode == StatusCodes.CLIENT_NETWORK_ERROR) return;

            if (_killSwitchService == null)
            {
                _killSwitchService = service;
                _killSwitchOperation = operation;
                _killSwitchErrorCount++;
            }
            else if (service == _killSwitchService && operation == _killSwitchOperation)
                _killSwitchErrorCount++;

            if (!_killSwitchEngaged && _killSwitchErrorCount >= _killSwitchThreshold)
            {
                _killSwitchEngaged = true;
                if (_clientRef.LoggingEnabled)
                {
                    _clientRef.Log("Client disabled due to repeated errors from a single API call: " + service + " | " + operation);
                }
            }

            //Authentication check for kill switch. 
            //did the client make an authentication call?
            if (operation == ServiceOperation.Authenticate.Value)
            {
                if (_clientRef.LoggingEnabled)
                {
                    _clientRef.Log("Failed Authentication Call");
                }

                string num;
                num = _identicalFailedAuthenticationAttempts.ToString();
                if (_clientRef.LoggingEnabled)
                {
                    _clientRef.Log("Current number of identical failed authentications: " + num);
                }

                //have the attempts gone beyond the threshold?
                if (tooManyAuthenticationAttempts())
                {
                    //we have a problem now, it seems they are contiuously trying to authenticate and sending us too many errors.
                    //we are going to now engage the killswitch and disable the client. This will act differently however. client will not
                    //be able to send an authentication request for a time. 
                    if (_clientRef.LoggingEnabled)
                    {
                        _clientRef.Log("Too many identical repeat authentication failures");
                    }
                    _killSwitchEngaged = true;
                    ResetAuthenticationTimer();
                }

            }
        }

        private void ResetKillSwitch()
        {
            _killSwitchErrorCount = 0;
            _killSwitchService = null;
            _killSwitchOperation = null;

            //reset the amount of failed attempts upon a successful attempt
            _identicalFailedAuthenticationAttempts = 0;
            _recentResponseJsonData[0] = blankResponseData;
            _recentResponseJsonData[1] = blankResponseData;
        }

        /// <summary>
        /// Creates the request state object and sends the message bundle
        /// </summary>
        /// <returns>The and send next request bundle.</returns>
        private RequestState CreateAndSendNextRequestBundle()
        {
            RequestState requestState = null;
            lock (_serviceCallsWaiting)
            {
                if (_blockingQueue)
                {
                    _serviceCallsInProgress.InsertRange(0, _serviceCallsInTimeoutQueue);
                    _serviceCallsInTimeoutQueue.Clear();
                }
                else
                {
                    if (_serviceCallsWaiting.Count > 0)
                    {
                        //put auth first
                        ServerCall call = null;
                        int numMessagesWaiting = _serviceCallsWaiting.Count;
                        for (int i = 0; i < _serviceCallsWaiting.Count; ++i)
                        {
                            call = _serviceCallsWaiting[i];
                            if (call.GetType() == typeof(EndOfBundleMarker))
                            {
                                // if the first message is marker, just throw it away
                                if (i == 0)
                                {
                                    _serviceCallsWaiting.RemoveAt(0);
                                    --i;
                                    --numMessagesWaiting;
                                    continue;
                                }
                                else // otherwise cut off the bundle at the marker and toss marker away
                                {
                                    numMessagesWaiting = i;
                                    _serviceCallsWaiting.RemoveAt(i);
                                    break;
                                }
                            }

                            if (call.GetOperation() == ServiceOperation.Authenticate.Value)
                            {
                                if (i != 0)
                                {
                                    _serviceCallsWaiting.RemoveAt(i);
                                    _serviceCallsWaiting.Insert(0, call);
                                }

                                numMessagesWaiting = 1;
                                break;
                            }
                        }

                        if (numMessagesWaiting > _maxBundleMessages)
                        {
                            numMessagesWaiting = _maxBundleMessages;
                        }

                        if (numMessagesWaiting <= 0)
                        {
                            return null;
                        }

                        if (_serviceCallsInProgress.Count > 0)
                        {
                            // this should never happen
                            if (_clientRef.LoggingEnabled)
                            {
                                _clientRef.Log("ERROR - in progress queue is not empty but we're ready for the next message!");
                            }
                            _serviceCallsInProgress.Clear();
                        }

                        _serviceCallsInProgress = _serviceCallsWaiting.GetRange(0, numMessagesWaiting);
                        _serviceCallsWaiting.RemoveRange(0, numMessagesWaiting);
                    }
                }

                if (_serviceCallsInProgress.Count > 0)
                {
                    requestState = new RequestState();

                    // prepare json data for server
                    List<object> messageList = new List<object>();
                    bool isAuth = false;

                    ServerCall scIndex;
                    string operation = "";
                    string service = "";
                    for (int i = 0; i < _serviceCallsInProgress.Count; ++i)
                    {
                        scIndex = _serviceCallsInProgress[i];
                        operation = scIndex.GetOperation();
                        service = scIndex.GetService();
                        // don't send heartbeat if it was generated by comms (null callbacks)
                        // and there are other messages in the bundle - it's unnecessary
                        if (service.Equals(ServiceName.HeartBeat)
                            && operation.Equals(ServiceOperation.Read)
                            && (scIndex.GetCallback() == null
                                || scIndex.GetCallback().AreCallbacksNull()))
                        {
                            if (_serviceCallsInProgress.Count > 1)
                            {
                                _serviceCallsInProgress.RemoveAt(i);
                                --i;
                                continue;
                            }
                        }

                        Dictionary<string, object> message = new Dictionary<string, object>();
                        message[OperationParam.ServiceMessageService.Value] = scIndex.Service;
                        message[OperationParam.ServiceMessageOperation.Value] = scIndex.Operation;
                        message[OperationParam.ServiceMessageData.Value] = scIndex.GetJsonData();

                        messageList.Add(message);

                        if (operation.Equals(ServiceOperation.Authenticate.Value))
                        {
                            requestState.PacketNoRetry = true;
                        }

                        if (operation.Equals(ServiceOperation.Authenticate.Value) ||
                            operation.Equals(ServiceOperation.ResetEmailPassword.Value) ||
                            operation.Equals(ServiceOperation.ResetEmailPasswordAdvanced.Value))
                        {
                            isAuth = true;
                        }

                        if (operation.Equals(ServiceOperation.FullReset.Value) ||
                            operation.Equals(ServiceOperation.Logout.Value))
                        {
                            requestState.PacketRequiresLongTimeout = true;
                        }
                    }

                    requestState.PacketId = _packetId;
                    _expectedIncomingPacketId = _packetId;
                    requestState.MessageList = messageList;
                    ++_packetId;

                    if (!_killSwitchEngaged && !tooManyAuthenticationAttempts())
                    {
                        if (_isAuthenticated || isAuth)
                        {
                            if (_clientRef.LoggingEnabled)
                            {
                                _clientRef.Log("SENDING REQUEST");
                            }
                            InternalSendMessage(requestState);
                        }
                        else
                        {
                            FakeErrorResponse(requestState, _cachedStatusCode, _cachedReasonCode, _cachedStatusMessage);
                            requestState = null;
                        }
                    }
                    else
                    {
                        if (tooManyAuthenticationAttempts())
                        {
                            FakeErrorResponse(requestState, StatusCodes.CLIENT_NETWORK_ERROR, ReasonCodes.CLIENT_DISABLED_FAILED_AUTH,
                                "Client has been disabled due to identical repeat Authentication calls that are throwing errors. Authenticating with the same credentials is disabled for 30 seconds");
                            requestState = null;
                        }
                        else
                        {
                            FakeErrorResponse(requestState, StatusCodes.CLIENT_NETWORK_ERROR, ReasonCodes.CLIENT_DISABLED,
                                "Client has been disabled due to repeated errors from a single API call");
                            requestState = null;
                        }
                    }
                }
            } // unlock _serviceCallsWaiting

            return requestState;
        }

        /// <summary>
        /// Creates a fake response to stop packets being sent to the server without a valid session.
        /// </summary>
        private void FakeErrorResponse(RequestState requestState, int statusCode, int reasonCode, string statusMessage)
        {
            Dictionary<string, object> packet = new Dictionary<string, object>();
            packet[OperationParam.ServiceMessagePacketId.Value] = requestState.PacketId;
            packet[OperationParam.ServiceMessageSessionId.Value] = SessionID;
            if (AppId != null && AppId.Length > 0)
            {
                packet[OperationParam.ServiceMessageGameId.Value] = AppId;
            }
            packet[OperationParam.ServiceMessageMessages.Value] = requestState.MessageList;

            string jsonRequestString = JsonWriter.Serialize(packet);

            if (_clientRef.LoggingEnabled)
            {
                _clientRef.Log(string.Format("{0} - {1}\n{2}", "REQUEST" + (requestState.Retries > 0 ? " Retry(" + requestState.Retries + ")" : ""), DateTime.Now, jsonRequestString));
            }

            ResetIdleTimer();

            TriggerCommsError(statusCode, reasonCode, statusMessage);
            _activeRequest = null;
        }

        /// <summary>
        /// Method creates the web request and sends it immediately.
        /// Relies upon the requestState PacketId and MessageList being
        /// set appropriately.
        /// </summary>
        /// <param name="requestState">Request state.</param>
        private void InternalSendMessage(RequestState requestState)
        {
#if DOT_NET
            // During retry, the RequestState is reused so we have to make sure its state goes back to PENDING.
            // Unity uses the info stored in the WWW object and it's recreated here so it's not an issue.
            requestState.DotNetRequestStatus = RequestState.eWebRequestStatus.STATUS_PENDING;
#endif

            // bundle up the data into a string
            Dictionary<string, object> packet = new Dictionary<string, object>();
            packet[OperationParam.ServiceMessagePacketId.Value] = requestState.PacketId;
            packet[OperationParam.ServiceMessageSessionId.Value] = SessionID;
            if (AppId != null && AppId.Length > 0)
            {
                packet[OperationParam.ServiceMessageGameId.Value] = AppId;
            }
            packet[OperationParam.ServiceMessageMessages.Value] = requestState.MessageList;

            string jsonRequestString = JsonWriter.Serialize(packet);
            string sig = CalculateMD5Hash(jsonRequestString + SecretKey);

            byte[] byteArray = Encoding.UTF8.GetBytes(jsonRequestString);

            requestState.Signature = sig;
            
            bool compressMessage = SupportsCompression &&                               // compression enabled
                                   ClientSideCompressionThreshold >= 0 &&               // server says we can compress
                                   byteArray.Length >= ClientSideCompressionThreshold;  // and byte array is greater or equal to the threshold

            //if the packet we're sending is larger than the size before compressing, then we want to compress it otherwise we're good to send it. AND we have to support compression
            if(compressMessage)
            {
                byteArray = Compress(byteArray);
            }

            requestState.ByteArray = byteArray;
            /*
            if (_debugPacketLossRate > 0.0)
            {
                System.Random r = new System.Random();
                requestState.LoseThisPacket = r.NextDouble() > _debugPacketLossRate;
            }
            */

            //if (!requestState.LoseThisPacket)
            {
#if !(DOT_NET)
                Dictionary<string, string> formTable = new Dictionary<string, string>();
#if USE_WEB_REQUEST
                UnityWebRequest request = UnityWebRequest.Post(ServerURL, formTable);
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.SetRequestHeader("X-SIG", sig);

                if (AppId != null && AppId.Length > 0)
                {
                    request.SetRequestHeader("X-APPID", AppId);
                }

                if(compressMessage)
                {
                    request.SetRequestHeader("Accept-Encoding", "gzip");
                    request.SetRequestHeader("Content-Encoding", "gzip");
                }          

                request.uploadHandler = new UploadHandlerRaw(byteArray);
                request.SendWebRequest();
#else
                formTable["Content-Type"] = "application/json; charset=utf-8";
                formTable["X-SIG"] = sig;
                if (AppId != null && AppId.Length > 0)
                {
                    formTable["X-APPID"] = AppId;
                }

                if(compressMessage)
                {
                    formTable["Accept-Encoding"] = "gzip";
                    formTable["Content-Encoding"] = "gzip";
                }

                WWW request = new WWW(ServerURL, byteArray, formTable);
#endif
                requestState.WebRequest = request;
#else

                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, new Uri(ServerURL));

                req.Content = new ByteArrayContent(byteArray);

                if(compressMessage)
                {
                    req.Headers.Add("Accept-Encoding", "gzip");
                    req.Content.Headers.Add("Content-Encoding", "gzip");
                }

                req.Headers.Add("X-SIG", sig);
                if (AppId != null && AppId.Length > 0) 
                {
                    req.Headers.Add("X-APPID", AppId);
                }

                req.Method = HttpMethod.Post;

                CancellationTokenSource source = new CancellationTokenSource();
                requestState.CancelToken = source;

                Task<HttpResponseMessage> httpRequest = _httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, source.Token);
                requestState.WebRequest = httpRequest;
                httpRequest.ContinueWith(async (t) =>
                {
                    await AsyncHttpTaskCallback(t, requestState);
                });
#endif

                requestState.RequestString = jsonRequestString;
                requestState.TimeSent = DateTime.Now;

                ResetIdleTimer();

                if (_clientRef.LoggingEnabled)
                {
                    _clientRef.Log(string.Format("{0} - {1}\n{2}", "REQUEST" + (requestState.Retries > 0 ? " Retry(" + requestState.Retries + ")" : ""), DateTime.Now, jsonRequestString));
                }
            }
        }

        private byte[] Compress(byte[] raw)
        {
            var outputStream = new MemoryStream();
            using (var stream = new GZipStream(outputStream, CompressionMode.Compress, true))
            {
                stream.Write(raw, 0, raw.Length);
            }
            return outputStream.ToArray();
        }

        private byte[] Decompress(byte[] compressedBytes)
        {
            using (var inputStream = new MemoryStream(compressedBytes))
            using (var gZipStream = new GZipStream(inputStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                gZipStream.CopyTo(outputStream);
                outputStream.Read(compressedBytes, 0, compressedBytes.Length);
                return outputStream.ToArray();
            }
        }

        /// <summary>
        /// Resends a message bundle. Returns true if sent or
        /// false if max retries has been reached.
        /// </summary>
        /// <returns><c>true</c>, if message was resent, <c>false</c> if max retries hit.</returns>
        /// <param name="requestState">Request state.</param>
        private bool ResendMessage(RequestState requestState)
        {
            if (_activeRequest.Retries >= GetMaxRetriesForPacket(requestState))
            {
                return false;
            }
            ++_activeRequest.Retries;
            InternalSendMessage(requestState);
            return true;
        }

        /// <summary>
        /// Gets the web request status.
        /// </summary>
        /// <returns>The web request status.</returns>
        /// <param name="requestState">request state.</param>
        private RequestState.eWebRequestStatus GetWebRequestStatus(RequestState requestState)
        {
            RequestState.eWebRequestStatus status = RequestState.eWebRequestStatus.STATUS_PENDING;

            // for testing packet loss, some packets are flagged to be lost
            // and should always return status pending no matter what the real
            // status is
            if (_activeRequest.LoseThisPacket)
            {
                return status;
            }

#if !(DOT_NET)
            if (!string.IsNullOrEmpty(_activeRequest.WebRequest.error))
            {
                status = RequestState.eWebRequestStatus.STATUS_ERROR;
            }
#if USE_WEB_REQUEST
            else if (_activeRequest.WebRequest.downloadHandler.isDone)
            {
                status = RequestState.eWebRequestStatus.STATUS_DONE;
            }
#else
            else if (_activeRequest.WebRequest.isDone)
            {
                status = RequestState.eWebRequestStatus.STATUS_DONE;
            }
#endif
#else
            status = _activeRequest.DotNetRequestStatus;
#endif
            return status;
        }


        /// <summary>
        /// Gets the web request response.
        /// </summary>
        /// <returns>The web request response.</returns>
        /// <param name="requestState">request state.</param>
        private string GetWebRequestResponse(RequestState requestState)
        {
            string response = "";
#if !(DOT_NET)
            if (!string.IsNullOrEmpty(_activeRequest.WebRequest.error))
            {
                response = _activeRequest.WebRequest.error;
            }
            else
            {
#if USE_WEB_REQUEST
                if(_activeRequest.WebRequest.GetRequestHeader("Content-Encoding") != "gzip")
                {
                    response = _activeRequest.WebRequest.downloadHandler.text;
                }
                else 
                {
                    var decompressedByteArray = Decompress(_activeRequest.WebRequest.downloadHandler.data);
                    response = Encoding.UTF8.GetString(decompressedByteArray, 0, decompressedByteArray.Length);
                }
#else
                if(!_activeRequest.WebRequest.responseHeaders.ContainsKey("Content-Encoding") ||
                    _activeRequest.WebRequest.responseHeaders["Content-Encoding"] != "gzip")
                {
                    response = _activeRequest.WebRequest.text;
                }
                else
                {
                    var decompressedByteArray = Decompress(_activeRequest.WebRequest.bytes);
                    response = Encoding.UTF8.GetString(decompressedByteArray, 0, decompressedByteArray.Length);
                }
#endif
            }
#else
            response = _activeRequest.DotNetResponseString;
#endif
            return response;
        }

        /// <summary>
        /// Method returns the maximum retries for the given packet
        /// </summary>
        /// <returns>The maximum retries for the given packet.</returns>
        /// <param name="requestState">The active request.</param>
        private int GetMaxRetriesForPacket(RequestState requestState)
        {
            if (requestState.PacketNoRetry)
            {
                return 0;
            }
            return _packetTimeouts.Count;
        }

        /// <summary>
        /// Method staggers the packet timeout value based on the currentRetry
        /// </summary>
        /// <returns>The packet timeout.</returns>
        /// <param name="requestState">The active request.</param>
        private TimeSpan GetPacketTimeout(RequestState requestState)
        {
            if (requestState.PacketNoRetry)
            {
                return TimeSpan.FromSeconds(_authPacketTimeoutSecs);
            }

            int currentRetry = requestState.Retries;
            TimeSpan ret;

            // if this is a delete player, or logout we change the
            // timeout behaviour
            if (requestState.PacketRequiresLongTimeout)
            {
                // unused as default timeouts are now quite long
            }

            if (currentRetry >= _packetTimeouts.Count)
            {
                int secs = 10;
                if (_packetTimeouts.Count > 0)
                {
                    secs = _packetTimeouts[_packetTimeouts.Count - 1];
                }
                ret = TimeSpan.FromSeconds(secs);
            }
            else
            {
                ret = TimeSpan.FromSeconds(_packetTimeouts[currentRetry]);
            }

            return ret;
        }

        /// <summary>
        /// Sends the heartbeat.
        /// </summary>
        private void SendHeartbeat()
        {
            ServerCall sc = new ServerCall(ServiceName.HeartBeat, ServiceOperation.Read, null, null);
            AddToQueue(sc);
        }

#if DISABLE_SSL_CHECK
        private bool AcceptAllCertifications(object sender,
                                            System.Security.Cryptography.X509Certificates.X509Certificate certification,
                                             System.Security.Cryptography.X509Certificates.X509Chain chain,
                                             System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            // TODO: we should only be accepting certificates from places we deem safe [smrj]
            // right now accepting all! - not that secure!
            return true;
        }
#endif

        /// <summary>
        /// Adds a server call to the internal queue.
        /// </summary>
        /// <param name="call">The server call to execute</param>
        internal void AddToQueue(ServerCall call)
        {
            lock (_serviceCallsWaiting)
            {
                _serviceCallsWaiting.Add(call);
            }
        }

        /// <summary>
        /// Enables the communications layer.
        /// </summary>
        /// <param name="value">If set to <c>true</c> value.</param>
        public void EnableComms(bool value)
        {
            _enabled = value;
        }

        /// <summary>
        /// Resets the communication layer. Clients will need to
        /// reauthenticate after this method is called.
        /// </summary>
        internal void ResetCommunication()
        {
            lock (_serviceCallsWaiting)
            {
                _isAuthenticated = false;
                _blockingQueue = false;
                _serviceCallsWaiting.Clear();
                _serviceCallsInProgress.Clear();
                _serviceCallsInTimeoutQueue.Clear();
                _activeRequest = null;
                _clientRef.AuthenticationService.ProfileId = "";
                SessionID = "";
                _packetId = 0;
            }
        }


#if (DOT_NET)
        private async Task AsyncHttpTaskCallback(Task<HttpResponseMessage> asyncResult, RequestState requestState)
        {
            if (asyncResult.IsCanceled) return;

            HttpResponseMessage message = null;

            try
            {
                message = asyncResult.Result;
                HttpContent content = message.Content;

                //if its gzipped, the message is compressed
                if(content.Headers.ContentEncoding.ToString() != "gzip")
                {
                    requestState.DotNetResponseString = await content.ReadAsStringAsync();
                }
                else
                {
                    var byteArray = await content.ReadAsByteArrayAsync();
                    var decompressedByteArray = Decompress(byteArray);
                    requestState.DotNetResponseString = Encoding.UTF8.GetString(decompressedByteArray, 0, decompressedByteArray.Length);
                }
                
                // End the operation
                requestState.DotNetRequestStatus = message.IsSuccessStatusCode ?
                    RequestState.eWebRequestStatus.STATUS_DONE : RequestState.eWebRequestStatus.STATUS_ERROR;
            }

            catch (WebException wex)
            {
                if (_clientRef.LoggingEnabled)
                {
                    _clientRef.Log("GetResponseCallback - WebException: " + wex.ToString());
                }
                requestState.DotNetRequestStatus = RequestState.eWebRequestStatus.STATUS_ERROR;
            }
            catch (Exception ex)
            {
                if (_clientRef.LoggingEnabled)
                {
                    _clientRef.Log("GetResponseCallback - Exception: " + ex.ToString());
                }
                requestState.DotNetRequestStatus = RequestState.eWebRequestStatus.STATUS_ERROR;
            }

            // Release the HttpResponseMessage
            if (message != null) message.Dispose();
        }
#endif


        private string CalculateMD5Hash(string input)
        {
#if !(DOT_NET)
            MD5Unity.MD5 md5 = MD5Unity.MD5.Create();
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input); // UTF8, not ASCII
            byte[] hash = md5.ComputeHash(inputBytes);
#else
#if UWP
            Windows.Security.Cryptography.MD5 md5 = Windows.Security.Cryptography.MD5.Create();
#else
            System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
#endif
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input); // UTF8, not ASCII
            byte[] hash = md5.ComputeHash(inputBytes);
#endif

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Handles authenticate-specific data from successful request
        /// </summary>
        /// <param name="jsonString"></param>
        private void ProcessAuthenticate(Dictionary<string, object> jsonData)
        {
            //we want to extract the compressIfLarger amount
            if(jsonData.ContainsKey("compressIfLarger"))
                ClientSideCompressionThreshold = (int) jsonData["compressIfLarger"];

            long playerSessionExpiry = GetJsonLong(jsonData, OperationParam.AuthenticateServicePlayerSessionExpiry.Value, 5 * 60);
            long idleTimeout = (long)(playerSessionExpiry * 0.85);

            _idleTimeout = TimeSpan.FromSeconds(idleTimeout);

            object bundleMsgs = null;
            jsonData.TryGetValue("maxBundleMsgs", out bundleMsgs);
            if (bundleMsgs != null) _maxBundleMessages = (int)bundleMsgs;

            object killCount = null;
            jsonData.TryGetValue("maxKillCount", out killCount);
            if (killCount != null) _killSwitchThreshold = (int)killCount;

            ResetErrorCache();
            _isAuthenticated = true;
        }

        private void ProcessSwitchResponse(Dictionary<string, object> jsonData)
        {
            if (jsonData.ContainsKey("switchToAppId"))
            {
                string switchToAppId = (string)jsonData["switchToAppId"];
                AppId = switchToAppId;
            }
        }

        private static string GetJsonString(Dictionary<string, object> jsonData, string key, string defaultReturn)
        {
            object retVal = null;
            jsonData.TryGetValue(key, out retVal);
            return retVal != null ? retVal as string : defaultReturn;
        }


        private static long GetJsonLong(Dictionary<string, object> jsonData, string key, long defaultReturn)
        {
            object outObj = null;
            if (jsonData.TryGetValue(key, out outObj))
            {
                if (outObj is long)
                    return (long)outObj;
                if (outObj is int)
                    return (int)outObj;
            }

            return defaultReturn;
        }

        /// <summary>
        /// Resets the cached error message for local session error handling to default
        /// </summary>
        private void ResetErrorCache()
        {
            _cachedStatusCode = StatusCodes.FORBIDDEN;
            _cachedReasonCode = ReasonCodes.NO_SESSION;
            _cachedStatusMessage = "No session";
        }
    }

    #region Json parsing objects

    // Classes to handle JSON serialization - do not
    // try to make variables conform to coding standards as
    // they must match json variable name format exactly

    internal class JsonResponseBundleV2
    {
        public long packetId = 0;
        public Dictionary<string, object>[] responses = null;
        public Dictionary<string, object>[] events = null;

        public JsonResponseBundleV2()
        { }
    }

    internal class JsonResponseErrorBundleV2
    {
        public long packetId;
        public JsonErrorMessage[] responses;

        public JsonResponseErrorBundleV2()
        { }
    }

    internal class JsonErrorMessage
    {
        public int reason_code;
        public int status;
        public string status_message;
        public string severity = "ERROR";

        public JsonErrorMessage()
        { }

        public JsonErrorMessage(int status, int reasonCode, string statusMessage)
        {
            this.status = status;
            reason_code = reasonCode;
            status_message = statusMessage;
        }

        public string GetJsonString()
        {
            return JsonWriter.Serialize(this);
        }
    }
    #endregion
}
