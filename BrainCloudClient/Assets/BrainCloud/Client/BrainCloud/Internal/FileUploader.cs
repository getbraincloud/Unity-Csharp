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
    using System.IO;

#if !DOT_NET
    using UnityEngine;
    using BrainCloud.JsonFx.Json;
#if USE_WEB_REQUEST
#if UNITY_5_3
using UnityEngine.Experimental.Networking;
#else
    using UnityEngine.Networking;
#endif
#endif
#else
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
#endif
    /*
     * FileUploader is not supported in WebPlayer && WebGL
     */
    internal class FileUploader
    {
        public enum FileUploaderStatus
        {
            None,
            Pending,
            Uploading,
            CompleteFailed,
            CompleteSuccess
        };

        #region Properties
        public string UploadId { get; private set; }

        public double Progress { get; private set; }

        public long BytesTransferred { get { return (long)(TotalBytesToTransfer * Progress); } }

        public long TotalBytesToTransfer { get; set; }

        public FileUploaderStatus Status { get; private set; }

        public string Response { get; private set; }

        public int StatusCode { get; private set; }

        public int ReasonCode { get; private set; }

#if DOT_NET
        public HttpClient HttpClient { get; set; }
#endif
        #endregion

        //Silencing Unity WebPlayer && WebGL Warnings with Pragma Disable: FileUploader not supported on WebPlayer && WebGL
#pragma warning disable 649
        private BrainCloudClient _client;
        private string _sessionId;
        private string _localPath;
        private string _serverUrl;
        private string _fileName;
        private string _peerCode;
        private long _timeoutThreshold = 50;
        private int _timeout = 120;
        public string FileName
        {
            get => _fileName;
            set => _fileName = value;
        }
#pragma warning restore 649

        //transfer rate
        private const double TIME_INTERVAL = 0.25f;
        private double _transferElapsedTime;
        private long _transferRatesTotal;
        private long _lastTransferTotal;
        private long _transferRatePerSecond;

        //delta time
        private DateTime _lastTime;
        private double _deltaTime;

        //timeout
        private double _elapsedTime;
        private double _timeUnderMinRate;

#if USE_WEB_REQUEST
        private UnityWebRequest _request;
#elif !DOT_NET
        private WWW _request;
#else
        private CancellationTokenSource _cancelToken;
#endif

        public FileUploader(
            string uploadId,
            string localPath,
            string serverUrl,
            string sessionId,
            int timeout,
            int timeoutThreshold,
            BrainCloudClient client,
            string peerCode = "")
        {
            _client = client;

            UploadId = uploadId;
            _localPath = localPath;
            _serverUrl = serverUrl;
            _sessionId = sessionId;
            _peerCode = peerCode;

            _timeout = timeout;
            _timeoutThreshold = timeoutThreshold;
            Status = FileUploaderStatus.Pending;
        }

        public void Start()
        {
#if !UNITY_WEBPLAYER
#if !DOT_NET
            FileInfo info = new FileInfo(_localPath);
            byte[] file;
            if (info.Exists)
            {
                file = File.ReadAllBytes(_localPath);
            }
            else
            {
                file = System.Convert.FromBase64String(_localPath);
            }
            WWWForm postForm = new WWWForm();
            postForm.AddField("sessionId", _sessionId);

            if (_peerCode != "") postForm.AddField("peerCode", _peerCode);
            postForm.AddField("uploadId", UploadId);
            postForm.AddField("fileSize", file.Length);
            postForm.AddBinaryData("uploadFile", file, _fileName);

#if USE_WEB_REQUEST
            _request = UnityWebRequest.Post(_serverUrl, postForm);
            _request.SendWebRequest();
#else
            _request = new WWW(_serverUrl, postForm);
#endif
#else
            var requestMessage = new HttpRequestMessage()
            {
                RequestUri = new Uri(_serverUrl),
                Method = HttpMethod.Post
            };

            var requestContent = new MultipartFormDataContent();
            byte[] fileData = _client.FileService.FileStorage[_localPath];
            _client.FileService.FileStorage.Remove(_localPath);
            if (fileData == null)
            {
                ThrowError(ReasonCodes.FILE_DOES_NOT_EXIST,"Local path is wrong or file doesn't exist");
                return;
            }
            ProgressStream fileStream = new ProgressStream(new MemoryStream(fileData));
            fileStream.BytesRead += BytesReadCallback;

            requestContent.Add(new StringContent(_sessionId), "sessionId");
            if (_peerCode != "") requestContent.Add(new StringContent(_peerCode), "peerCode");
            requestContent.Add(new StringContent(UploadId), "uploadId");
            requestContent.Add(new StringContent(TotalBytesToTransfer.ToString()), "fileSize");
            requestContent.Add(new StreamContent(fileStream), "uploadFile", _fileName);
            
            requestMessage.Content = requestContent;
            _cancelToken = new CancellationTokenSource();
            Task<HttpResponseMessage> httpRequest = HttpClient.SendAsync(requestMessage, _cancelToken.Token);
            httpRequest.ContinueWith(async (t) =>
            {
                await AsyncHttpTaskCallback(t);
            });
#endif
            Status = FileUploaderStatus.Uploading;
            if (_client.LoggingEnabled)
            {
                _client.Log("Started upload of " + _fileName);
            }
            _lastTime = DateTime.Now;
#endif
        }

#if (DOT_NET)
        private async Task AsyncHttpTaskCallback(Task<HttpResponseMessage> asyncResult)
        {
            if (asyncResult.IsCanceled) return;            

            bool isError = false;
            HttpResponseMessage message = null;

            //a callback method to end receiving the data
            try
            {
                message = asyncResult.Result;
                HttpContent content = message.Content;

                // End the operation
                Response = await content.ReadAsStringAsync();
                StatusCode = (int)message.StatusCode;
                Status = FileUploaderStatus.CompleteSuccess;
                if (_client.LoggingEnabled)
                {
                    _client.Log("Uploaded " + _fileName + " in " + _elapsedTime.ToString("0.0##") + " seconds");
                }
            }
            catch (WebException wex)
            {
                Response = CreateErrorString(StatusCode, ReasonCode, wex.Message);
            }
            catch (Exception ex)
            {
                Response = CreateErrorString(StatusCode, ReasonCode, ex.Message);
            }

            if (isError)
            {
                Status = FileUploaderStatus.CompleteFailed;
                StatusCode = StatusCodes.CLIENT_NETWORK_ERROR;
                ReasonCode = ReasonCodes.CLIENT_UPLOAD_FILE_UNKNOWN;
            }

            // Release the HttpResponseMessage
            if(message != null) message.Dispose();
        }

        private void BytesReadCallback(object sender, ProgressStreamReportEventArgs args)
        {
            Progress = (double)args.StreamPosition / args.StreamLength;
        }
#endif

        public void CancelUpload()
        {
#if USE_WEB_REQUEST
            _request.Abort();
#elif !DOT_NET
            _request = null;
#else
            _cancelToken.Cancel();
#endif
            Status = FileUploaderStatus.CompleteFailed;
            StatusCode = StatusCodes.CLIENT_NETWORK_ERROR;
            ReasonCode = ReasonCodes.CLIENT_UPLOAD_FILE_CANCELLED;
            Response = CreateErrorString(StatusCode, ReasonCode, "Upload of " + _fileName + " cancelled by user");
            
            if (_client.LoggingEnabled)
            {
                _client.Log("Upload of " + _fileName + " cancelled by user");
            }
        }

        public void Update()
        {
            UpdateDeltaTime();
            _elapsedTime += _deltaTime;

            UpdateTransferRate();
#if !DOT_NET && (UNITY_IOS || UNITY_ANDROID)
            CheckTimeout();
#endif
            if (Status == FileUploaderStatus.CompleteFailed || Status == FileUploaderStatus.CompleteSuccess)
            {
#if !DOT_NET && USE_WEB_REQUEST
                CleanupRequest();
#endif
                return;
            }

#if !DOT_NET
            Progress = _request.uploadProgress;
            if (_request.isDone) HandleResponse();
#endif
        }

#if !DOT_NET
        private void HandleResponse()
        {
            _transferRatePerSecond = 0;

#if USE_WEB_REQUEST
            StatusCode = (int)_request.responseCode;
#else
            if (_request.responseHeaders.ContainsKey("STATUS"))
            {
                string code = _request.responseHeaders["STATUS"].Split(' ')[1];
                StatusCode = int.Parse(code);
            }
            else StatusCode = StatusCodes.CLIENT_NETWORK_ERROR;
#endif
            if (StatusCode != StatusCodes.OK)
            {
                Status = FileUploaderStatus.CompleteFailed;

                if (_request.error != null)
                {
                    ReasonCode = ReasonCodes.CLIENT_UPLOAD_FILE_UNKNOWN;
                    Response = CreateErrorString(StatusCode, ReasonCode, _request.error);
                }
                else
#if USE_WEB_REQUEST
                    Response = _request.downloadHandler.text;
#else
                    Response = _request.text;
#endif
                JsonErrorMessage resp = null;

                try { resp = JsonReader.Deserialize<JsonErrorMessage>(Response); }
                catch (JsonDeserializationException e)
                {
                    if (_client.LoggingEnabled)
                    {
                        _client.Log(e.Message);
                    }
                }

                if (resp != null)
                    ReasonCode = resp.reason_code;
                else
                {
                    ReasonCode = ReasonCodes.CLIENT_UPLOAD_FILE_UNKNOWN;
                    Response = CreateErrorString(StatusCode, ReasonCode, Response);
                }
            }
            else
            {
                Status = FileUploaderStatus.CompleteSuccess;

#if USE_WEB_REQUEST
                Response = _request.downloadHandler.text;
#else
                Response = _request.text;
#endif
                if (_client.LoggingEnabled)
                {
                    _client.Log("Uploaded " + _fileName + " in " + _elapsedTime.ToString("0.0##") + " seconds");
                }
            }

#if USE_WEB_REQUEST
            CleanupRequest();
#endif
        }
#endif

        private void UpdateTransferRate()
        {
            _transferElapsedTime += _deltaTime;

            if (_transferElapsedTime > TIME_INTERVAL)
            {
                _transferRatePerSecond = (long)(_transferRatesTotal / _transferElapsedTime);
                _transferRatesTotal = 0;
                _transferElapsedTime = 0;
            }
            else
            {
                _transferRatesTotal += BytesTransferred - _lastTransferTotal;
                _lastTransferTotal = BytesTransferred;
            }
        }

        private void CheckTimeout()
        {
            if (_transferRatePerSecond < _timeoutThreshold)
                _timeUnderMinRate += _deltaTime;
            else
                _timeUnderMinRate = 0.0;

            if (_timeUnderMinRate > _timeout)
                ThrowError(ReasonCodes.CLIENT_UPLOAD_FILE_TIMED_OUT, "Upload of " + _fileName + " failed due to timeout.");
        }

        private void UpdateDeltaTime()
        {
            _deltaTime = DateTime.Now.Subtract(_lastTime).TotalSeconds;
            _lastTime = DateTime.Now;
        }

        private void ThrowError(int reasonCode, string message)
        {
            Status = FileUploaderStatus.CompleteFailed;
            StatusCode = StatusCodes.CLIENT_NETWORK_ERROR;
            ReasonCode = reasonCode;
            Response = CreateErrorString(StatusCode, ReasonCode, message);
        }

        private string CreateErrorString(int statusCode, int reasonCode, string message)
        {
            return new JsonErrorMessage(statusCode, reasonCode, message).GetJsonString();
        }

#if USE_WEB_REQUEST
        private void CleanupRequest()
        {
            if (_request == null) return;
            _request.Dispose();
            _request = null;
        }
#endif
    }
}
