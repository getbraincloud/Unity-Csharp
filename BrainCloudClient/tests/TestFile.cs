using NUnit.Core;
using NUnit.Framework;
using BrainCloud;
using System.Collections.Generic;
using BrainCloud.JsonFx.Json;
using System.IO;
using System.Threading;
using System;
using System.Net;
using System.Text;

namespace BrainCloudTests
{
    [TestFixture]
    public class TestFile : TestFixtureBase
    {
        private const string _cloudPath = "";

        private int _returnCount = 0;
        private int _successCount = 0;
        private int _failCount = 0;

        [TearDown]
        public void Cleanup()
        {
            _bc.Client.DeregisterFileUploadCallbacks();
            DeleteAllFiles();
            _returnCount = 0;
            _successCount = 0;
            _failCount = 0;
        }

        [Test]
        public void TestListUserFiles()
        {
            TestResult tr = new TestResult(_bc);

            _bc.FileService.ListUserFiles(tr.ApiSuccess, tr.ApiError);

            tr.Run();
        }

        [Test]
        public void TestSimpleUpload()
        {
            TestResult tr = new TestResult(_bc);
            _bc.Client.RegisterFileUploadCallbacks(FileCallbackSuccess, FileCallbackFail);

            FileInfo info = new FileInfo(CreateFile(4024));
            
            _bc.FileService.UploadFile(
                _cloudPath,
                info.Name,
                true,
                true,
                info.FullName,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();

            WaitForReturn(new[] { GetUploadId(tr.m_response) });

            Assert.IsFalse(_failCount > 0);
        }
        
        [Test]
        public void TestUploadFromMemory()
        {
            TestResult tr = new TestResult(_bc);
            _bc.Client.RegisterFileUploadCallbacks(FileCallbackSuccess, FileCallbackFail);

            string fileName = "testFile.txt";
            string fileContent = "Hello, I'm a file";
            byte[] fileData = ConvertByteFromFile(fileContent);

            _bc.FileService.UploadFileFromMemory(
                _cloudPath,
                fileName,
                true,
                true,
                fileData,
                tr.ApiSuccess, tr.ApiError);
            
            tr.Run();

            WaitForReturn(new[] { GetUploadId(tr.m_response) });

            Assert.IsFalse(_failCount > 0);
        }

        [Test]
        public void TestUploadFromMemoryMultiFiles()
        {
            TestResult tr = new TestResult(_bc);
            _bc.Client.RegisterFileUploadCallbacks(FileCallbackSuccess, FileCallbackFail);
            
            string fileName1 = "testFile1.txt";
            string fileContent1 = "Hello, I'm a file !";
            byte[] fileData1 = ConvertByteFromFile(fileContent1);
            _bc.FileService.UploadFileFromMemory(
                _cloudPath,
                fileName1,
                true,
                true,
                fileData1,
                tr.ApiSuccess, tr.ApiError);

            string fileName2 = "testFile2.txt";
            string fileContent2 = "Hey look ! Another file !";
            byte[] fileData2 = ConvertByteFromFile(fileContent2);
            _bc.FileService.UploadFileFromMemory(
                _cloudPath,
                fileName2,
                true,
                true,
                fileData2,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();

            WaitForReturn(new[] { GetUploadId(tr.m_response) });

            Assert.IsFalse(_failCount > 0);
        }

        private string GetFullPath(string cloudPath, string cloudFileName)
        {
            string serverUrl = ServerUrl.Replace("/dispatcherv2", "");
            return serverUrl +
                   "/downloader/bc/g/" +
                    _bc.Client.AppId +
                   "/u/" +
                    _bc.Client.AuthenticationService.ProfileId +
                   "/f/" +
                    cloudPath +
                   "/" +
                    cloudFileName;
        }

        [Test]
        public void TestSimpleUploadFailedFromPrivacySettings()
        {
            String cloudPath = "test";
        
            TestResult tr = new TestResult(_bc);
            _bc.Client.RegisterFileUploadCallbacks(FileCallbackSuccess, FileCallbackFail);
        
            FileInfo info = new FileInfo(CreateFile(4024));
        
            _bc.FileService.UploadFile(
                cloudPath,
                info.Name,
                false,
                true,
                info.FullName,
                tr.ApiSuccess, tr.ApiError);
        
            tr.Run();

            WaitForReturn(new[] { GetUploadId(tr.m_response) });

            Assert.IsFalse(_failCount > 0);

            Thread.Sleep(2000);

            WebClient webClient = new WebClient();
            string name = info.Name;
            string fullPath = GetFullPath(cloudPath, name);

            try
            {
                webClient.DownloadFile(fullPath, name);
            }
            catch(Exception e)
            {
                return;
            }

            Assert.IsFalse(true);
        }

        [Test]
        public void TestUploadSimpleFileAndCancel()
        {
            DeleteAllFiles();

            TestResult tr = new TestResult(_bc);
            _bc.Client.RegisterFileUploadCallbacks(FileCallbackSuccess, FileCallbackFail);

            FileInfo info = new FileInfo(CreateFile(12 * 1024));

            _bc.FileService.UploadFile(
                _cloudPath,
                "testFileCANCEL.dat",
                true,
                true,
                info.FullName,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();

            WaitForReturn(new[] { GetUploadId(tr.m_response) }, 500);

            Thread.Sleep(5000);

            _bc.FileService.ListUserFiles(tr.ApiSuccess, tr.ApiError);
            tr.Run();

            var fileList = ((object[])((Dictionary<string, object>)tr.m_response["data"])["fileList"]);

            Console.WriteLine("\nDid fail = " + (_failCount > 0));
            Console.WriteLine("File list length  = " + fileList.Length + "\n");

            Assert.IsFalse(_failCount <= 0 || fileList.Length > 0);
        }

        [Test]
        public void TestDeleteUserFile()
        {
            TestResult tr = new TestResult(_bc);
            _bc.Client.RegisterFileUploadCallbacks(FileCallbackSuccess, FileCallbackFail);

            FileInfo info = new FileInfo(CreateFile(4024));

            _bc.FileService.UploadFile(
                _cloudPath,
                info.Name,
                true,
                true,
                info.FullName,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();

            WaitForReturn(new[] { GetUploadId(tr.m_response) });

            Assert.IsFalse(_failCount > 0);

            _bc.FileService.DeleteUserFile(
                GetCloudPath(tr.m_response),
                info.Name,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();
        }


        [Test]
        public void TestGetCdnUrl()
        {
            TestResult tr = new TestResult(_bc);
            _bc.Client.RegisterFileUploadCallbacks(FileCallbackSuccess, FileCallbackFail);

            FileInfo info = new FileInfo(CreateFile(1024));

            _bc.FileService.UploadFile(
                _cloudPath,
                info.Name,
                true,
                true,
                info.FullName,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();

            WaitForReturn(new[] { GetUploadId(tr.m_response) });

            Assert.IsFalse(_failCount > 0);

            _bc.FileService.GetCDNUrl(
                GetCloudPath(tr.m_response),
                info.Name,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();
        }

        [Test]
        public void TestDeleteUserFiles()
        {
            TestResult tr = new TestResult(_bc);
            _bc.Client.RegisterFileUploadCallbacks(FileCallbackSuccess, FileCallbackFail);

            FileInfo info = new FileInfo(CreateFile(4024));

            _bc.FileService.UploadFile(
                _cloudPath,
                info.Name,
                true,
                true,
                info.FullName,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();

            WaitForReturn(new[] { GetUploadId(tr.m_response) });

            Assert.IsFalse(_failCount > 0);

            _bc.FileService.DeleteUserFiles(
                GetCloudPath(tr.m_response),
                true,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();
        }

        [Test]
        public void TestUploadMultiple()
        {
            TestResult tr = new TestResult(_bc);
            _bc.Client.RegisterFileUploadCallbacks(FileCallbackSuccess, FileCallbackFail);

            FileInfo info1 = new FileInfo(CreateFile(4096, "multiUpload1.dat"));
            FileInfo info2 = new FileInfo(CreateFile(4096, "multiUpload2.dat"));

            _bc.FileService.UploadFile(
                _cloudPath,
                info1.Name,
                true,
                true,
                info1.FullName,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();
            string id1 = GetUploadId(tr.m_response);

            _bc.FileService.UploadFile(
                _cloudPath,
                info2.Name,
                true,
                true,
                info2.FullName,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();

            WaitForReturn(new[] { id1, GetUploadId(tr.m_response) });

            Assert.IsFalse(_failCount > 0);
        }

        private void FileCallbackSuccess(string uploadId, string jsonData)
        {
            _returnCount++;
            _successCount++;
        }

        private void FileCallbackFail(string uploadId, int statusCode, int reasonCode, string jsonData)
        {
            _returnCount++;
            _failCount++;
        }

        private void WaitForReturn(string[] uploadIds, int cancelTime = -1)
        {
            int count = 0;
            bool sw = true;

            BrainCloudClient client = _bc.Client;

            client.Update();
            while (_returnCount < uploadIds.Length && count < 1000 * 30)
            {

                for (int i = 0; i < uploadIds.Length; i++)
                {
                    double progress = client.FileService.GetUploadProgress(uploadIds[i]);

                    if (progress > -1 && sw)
                    {
                        string logStr = "File " + (i + 1) + " Progress: " +
                            progress + " | " +
                            client.FileService.GetUploadBytesTransferred(uploadIds[i]) + "/" +
                            client.FileService.GetUploadTotalBytesToTransfer(uploadIds[i]);
                        Console.WriteLine(logStr);
                    }

                    if (cancelTime > 0 && progress > 0.05)
                    {
                        client.FileService.CancelUpload(uploadIds[i]);
                    }
                }

                client.Update();
                sw = !sw;

                Thread.Sleep(150);
                count += 150;
            }
        }

        private string GetUploadId(Dictionary<string, object> response)
        {
            var fileDetails = ((Dictionary<string, object>)((Dictionary<string, object>)response["data"])["fileDetails"]);
            return (string)(fileDetails["uploadId"]);
        }

        private string GetCloudPath(Dictionary<string, object> response)
        {
            var fileDetails = ((Dictionary<string, object>)((Dictionary<string, object>)response["data"])["fileDetails"]);
            return (string)(fileDetails["cloudPath"]);
        }

        private void DeleteAllFiles()
        {
            TestResult tr = new TestResult(_bc);
            _bc.FileService.DeleteUserFiles("", true, tr.ApiSuccess, tr.ApiError);
            tr.Run();
        }
        
        /// <summary>
        /// Creates a test file filled with garbage
        /// </summary>
        /// <param name="size">Size of the file in KB</param>
        /// <returns>Full path to the file</returns>
        private string CreateFile(int size, string name = "testFile.dat")
        {
            string path = Path.Combine(Path.GetTempPath() + name);

            using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fileStream.SetLength(1024 * size);
            }

            return path;
        }

        private byte[] ConvertByteFromFile(string fileContent)
        {
            if (Uri.IsWellFormedUriString(fileContent, UriKind.Absolute))
            {
                Stream info = new FileStream(fileContent, FileMode.Open);
                byte[] fileData = new Byte[(int) info.Length];
                info.Seek(0, SeekOrigin.Begin);
                info.Read(fileData, 0, (int) info.Length);
                info.Close();
                return fileData;
            }

            return Encoding.ASCII.GetBytes(fileContent);
        }
    }
}