//----------------------------------------------------
// brainCloud client source code
// Copyright 2016 bitHeads, inc.
//----------------------------------------------------

namespace BrainCloud
{

using BrainCloud.Internal;
using System.Collections.Generic;
using System.IO;
using System;


    public class BrainCloudFile
    {
        private BrainCloudClient _client;
        public Dictionary<string, byte[]> FileStorage = new Dictionary<string, byte[]>();
        
        public BrainCloudFile(BrainCloudClient client)
        {
            _client = client;
        }

        /// <summary>
        /// Prepares a user file upload. On success the file will begin uploading
        /// to the brainCloud server.To be informed of success/failure of the upload
        /// register an IFileUploadCallback with the BrainCloudClient class.
        /// </summary>
        /// <param name="cloudPath">The desired cloud path of the file</param>
        /// <param name="cloudFilename">The desired cloud fileName of the file</param>
        /// <param name="shareable">True if the file is shareable</param>
        /// <param name="replaceIfExists">Whether to replace file if it exists</param>
        /// <param name="localPath">The path and fileName of the local file</param>
        /// <param name="success">The success callback</param>
        /// <param name="failure">The failure callback</param>
        /// <param name="cbObject">The callback object</param>
        [Obsolete("This has been deprecated use UploadFileFromMemory instead - removal after June 22nd 2022")]
        public bool UploadFile(
            string cloudPath,
            string cloudFilename,
            bool shareable,
            bool replaceIfExists,
            string localPath,
            SuccessCallback success = null,
            FailureCallback failure = null,
            object cbObject = null)
        {
#if UNITY_WEBPLAYER || UNITY_WEBGL
            throw new Exception("FileUpload API is not supported on Web builds use FileUploadFromMemory instead");
#else
            Stream info = new FileStream(localPath,FileMode.Open);

            if (info.Length == 0)
            {
                _client.Log("File at " + localPath + " does not exist");
                return false;
            }
            byte[] fileData = new Byte[(int)info.Length];
            info.Seek(0, SeekOrigin.Begin);
            info.Read(fileData, 0, (int)info.Length);
            info.Close();

            return UploadFileFromMemory(cloudFilename, cloudFilename, shareable, replaceIfExists, fileData, success,
                failure, cbObject);
#endif
        }

        /// <summary>
        /// Prepares a user file upload from memory, allowing the user to bypass 
        /// the need to read or write on disk before uploading. On success the file will begin uploading
        /// to the brainCloud server.To be informed of success/failure of the upload
        /// register an IFileUploadCallback with the BrainCloudClient class.
        /// </summary>
        /// <param name="cloudPath">The desired cloud path of the file</param>
        /// <param name="cloudFilename">The desired cloud fileName of the file</param>
        /// <param name="shareable">True if the file is shareable</param>
        /// <param name="replaceIfExists">Whether to replace file if it exists</param>
        /// <param name="fileData">The file memory data in byte[]</param>
        /// <param name="success">The success callback</param>
        /// <param name="failure">The failure callback</param>
        /// <param name="cbObject">The callback object</param>
        public bool UploadFileFromMemory(
            string cloudPath,
            string cloudFilename,
            bool shareable,
            bool replaceIfExists,
            byte[] fileData,
            SuccessCallback success = null,
            FailureCallback failure = null,
            object cbObject = null)
        {   
            if (fileData.Length == 0)
            {
                _client.Log("File data is empty");
                return false;
            }
            string guid = Guid.NewGuid().ToString();
            _client.FileService.FileStorage.Add(guid, fileData);
            
            Dictionary<string, object> data = new Dictionary<string, object>();
            data[OperationParam.UploadLocalPath.Value] = guid;
            data[OperationParam.UploadCloudFilename.Value] = cloudFilename;
            data[OperationParam.UploadCloudPath.Value] = cloudPath;
            data[OperationParam.UploadShareable.Value] = shareable;
            data[OperationParam.UploadReplaceIfExists.Value] = replaceIfExists;
            data[OperationParam.UploadFileSize.Value] = fileData.Length;
            
            ServerCallback callback = BrainCloudClient.CreateServerCallback(success, failure, cbObject);
            ServerCall sc = new ServerCall(ServiceName.File, ServiceOperation.PrepareUserUpload, data, callback);
            _client.SendRequest(sc);

            return true;
        }

        /// <summary>
        /// Method cancels an upload. If an IFileUploadCallback has been registered with the BrainCloudClient class,
        /// the fileUploadFailed callback method will be called once the upload has been canceled.
        /// NOTE: The upload will still continue in the background on versions of Unity before 5.3
        /// and on Unity mobile platforms.
        /// </summary>
        /// <param name="uploadId">Upload ID of the file to cancel</param>
        public void CancelUpload(string uploadId)
        {
            _client.Comms.CancelUpload(uploadId);
        }

        /// <summary>
        /// Returns the progress of the given upload from 0.0 to 1.0 or -1 if upload not found.
        /// NOTE: This will always return 1 on Unity mobile platforms.
        /// </summary>
        /// <param name="uploadId">The id of the upload</param>
        public double GetUploadProgress(string uploadId)
        {
            return _client.Comms.GetUploadProgress(uploadId);
        }

        /// <summary>
        /// Returns the number of bytes uploaded or -1 if upload not found.
        /// NOTE: This will always return the total bytes to transfer on Unity mobile platforms.
        /// </summary>
        /// <param name="uploadId">The id of the upload</param>
        public long GetUploadBytesTransferred(string uploadId)
        {
            return _client.Comms.GetUploadBytesTransferred(uploadId);
        }

        /// <summary>
        /// Returns the total number of bytes that will be uploaded or -1 if upload not found.
        /// </summary>
        /// <param name="uploadId">The id of the upload</param>
        public long GetUploadTotalBytesToTransfer(string uploadId)
        {
            return _client.Comms.GetUploadTotalBytesToTransfer(uploadId);
        }

        /// <summary>
        /// List all user files
        /// </summary>
        /// <param name="success">The success callback</param>
        /// <param name="failure">The failure callback</param>
        /// <param name="cbObject">The callback object</param>
        public void ListUserFiles(
            SuccessCallback success = null,
            FailureCallback failure = null,
            object cbObject = null)
        {
            ListUserFiles(null, null, success, failure, cbObject);
        }

        /// <summary>
        /// List user files from the given cloud path
        /// </summary>
        /// <param name="cloudPath">File path</param>
        /// <param name="recurse">Whether to recurse down the path</param>
        /// <param name="success">The success callback</param>
        /// <param name="failure">The failure callback</param>
        /// <param name="cbObject">The callback object</param>
        public void ListUserFiles(
            string cloudPath,
            bool? recurse,
            SuccessCallback success = null,
            FailureCallback failure = null,
            object cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();

            if (Util.IsOptionalParameterValid(cloudPath))
                data[OperationParam.UploadPath.Value] = cloudPath;
            if (recurse.HasValue)
                data[OperationParam.UploadRecurse.Value] = recurse.Value;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(success, failure, cbObject);
            ServerCall sc = new ServerCall(ServiceName.File, ServiceOperation.ListUserFiles, data, callback);
            _client.SendRequest(sc);
        }

        /// <summary>
        /// Deletes a single user file.
        /// </summary>
        /// <param name="cloudPath">File path</param>
        /// <param name="cloudFileName"></param>
        /// <param name="success">The success callback</param>
        /// <param name="failure">The failure callback</param>
        /// <param name="cbObject">The callback object</param>
        public void DeleteUserFile(
            string cloudPath,
            string cloudFileName,
            SuccessCallback success = null,
            FailureCallback failure = null,
            object cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();

            data[OperationParam.UploadCloudPath.Value] = cloudPath;
            data[OperationParam.UploadCloudFilename.Value] = cloudFileName;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(success, failure, cbObject);
            ServerCall sc = new ServerCall(ServiceName.File, ServiceOperation.DeleteUserFile, data, callback);
            _client.SendRequest(sc);
        }

        /// <summary>
        /// Delete multiple user files
        /// </summary>
        /// <param name="cloudPath">File path</param>
        /// <param name="recurse">Whether to recurse down the path</param>
        /// <param name="success">The success callback</param>
        /// <param name="failure">The failure callback</param>
        /// <param name="cbObject">The callback object</param>
        public void DeleteUserFiles(
            string cloudPath,
            bool recurse,
            SuccessCallback success = null,
            FailureCallback failure = null,
            object cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();

            data[OperationParam.UploadCloudPath.Value] = cloudPath;
            data[OperationParam.UploadRecurse.Value] = recurse;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(success, failure, cbObject);
            ServerCall sc = new ServerCall(ServiceName.File, ServiceOperation.DeleteUserFiles, data, callback);
            _client.SendRequest(sc);
        }

        /// <summary>
        /// Returns the CDN URL for a file object.
        /// </summary>
        /// <param name="cloudPath">File path</param>
        /// <param name="cloudFilename">Name of file</param>
        /// <param name="success">The success callback</param>
        /// <param name="failure">The failure callback</param>
        /// <param name="cbObject">The callback object</param>
        public void GetCDNUrl(
            string cloudPath,
            string cloudFilename,
            SuccessCallback success = null,
            FailureCallback failure = null,
            object cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();

            data[OperationParam.UploadCloudPath.Value] = cloudPath;
            data[OperationParam.UploadCloudFilename.Value] = cloudFilename;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(success, failure, cbObject);
            ServerCall sc = new ServerCall(ServiceName.File, ServiceOperation.GetCdnUrl, data, callback);
            _client.SendRequest(sc);
        }
    }
}

