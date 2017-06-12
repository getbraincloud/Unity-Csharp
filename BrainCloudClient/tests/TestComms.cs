using NUnit.Core;
using NUnit.Framework;
using BrainCloud;
using System.Collections.Generic;
using System;
using System.Threading;

namespace BrainCloudTests
{
    [TestFixture]
    public class TestComms : TestFixtureNoAuth
    {
        private int _globalErrorCount;

        [TearDown]
        public void Cleanup()
        {
            BrainCloudClient.Instance.DeregisterGlobalErrorCallback();
            _globalErrorCount = 0;
        }

        [Test]
        public void TestNoSession()
        {
            //BrainCloudClient.Instance.ResetCommunication();
            //BrainCloudClient.Instance.Initialize(_serverUrl, _secret, _appId, _version);
            //BrainCloudClient.Instance.EnableLogging(true);

            TestResult tr = new TestResult();

            BrainCloudClient.Instance.AuthenticationService.AuthenticateUniversal(
                GetUser(Users.UserA).Id,
                GetUser(Users.UserA).Password,
                false, tr.ApiSuccess, tr.ApiError);
            tr.Run();

            BrainCloudClient.Instance.TimeService.ReadServerTime(tr.ApiSuccess, tr.ApiError);
            tr.Run();

            BrainCloudClient.Instance.PlayerStateService.Logout(tr.ApiSuccess, tr.ApiError);
            tr.Run();

            BrainCloudClient.Instance.TimeService.ReadServerTime(tr.ApiSuccess, tr.ApiError);
            tr.RunExpectFail(StatusCodes.FORBIDDEN, ReasonCodes.NO_SESSION);
        }

        [Test]
        public void TestSessionTimeout()
        {
            //BrainCloudClient.Instance.ResetCommunication();
            //BrainCloudClient.Instance.Initialize(_serverUrl, _secret, _appId, _version);
            //BrainCloudClient.Instance.EnableLogging(true);

            TestResult tr = new TestResult();

            BrainCloudClient.Instance.AuthenticationService.AuthenticateUniversal(
                GetUser(Users.UserA).Id,
                GetUser(Users.UserA).Password,
                false, tr.ApiSuccess, tr.ApiError);
            tr.Run();

            Console.WriteLine("\nWaiting for session to expire...");
            Thread.Sleep(61 * 1000);

            BrainCloudClient.Instance.TimeService.ReadServerTime(tr.ApiSuccess, tr.ApiError);
            tr.RunExpectFail(StatusCodes.FORBIDDEN, ReasonCodes.PLAYER_SESSION_EXPIRED);

            BrainCloudClient.Instance.TimeService.ReadServerTime(tr.ApiSuccess, tr.ApiError);
            tr.RunExpectFail(StatusCodes.FORBIDDEN, ReasonCodes.PLAYER_SESSION_EXPIRED);
        }

        [Test]
        public void TestBadUrl()
        {
            BrainCloudClient.Instance.Initialize(ServerUrl + "unitTestFail", Secret, AppId, Version);
            BrainCloudClient.Instance.EnableLogging(true);

            DateTime timeStart = DateTime.Now;
            TestResult tr = new TestResult();
            tr.SetTimeToWaitSecs(120);
            BrainCloudClient.Instance.AuthenticationService.AuthenticateUniversal("abc", "abc", true, tr.ApiSuccess, tr.ApiError);
            tr.RunExpectFail(StatusCodes.CLIENT_NETWORK_ERROR, ReasonCodes.CLIENT_NETWORK_ERROR_TIMEOUT);

            DateTime timeEnd = DateTime.Now;
            TimeSpan delta = timeEnd.Subtract(timeStart);
            Assert.True(delta >= TimeSpan.FromSeconds(13) && delta <= TimeSpan.FromSeconds(17));
        }

        [Test]
        public void TestPacketTimeouts()
        {
            try
            {
                BrainCloudClient.Instance.Initialize(ServerUrl + "unitTestFail", Secret, AppId, Version);
                BrainCloudClient.Instance.EnableLogging(true);
                BrainCloudClient.Instance.SetPacketTimeouts(new List<int> { 3, 3, 3 });

                DateTime timeStart = DateTime.Now;
                TestResult tr = new TestResult();
                tr.SetTimeToWaitSecs(120);
                BrainCloudClient.Instance.AuthenticationService.AuthenticateUniversal("abc", "abc", true, tr.ApiSuccess, tr.ApiError);
                tr.RunExpectFail(StatusCodes.CLIENT_NETWORK_ERROR, ReasonCodes.CLIENT_NETWORK_ERROR_TIMEOUT);


                DateTime timeEnd = DateTime.Now;
                TimeSpan delta = timeEnd.Subtract(timeStart);
                if (delta < TimeSpan.FromSeconds(8) && delta > TimeSpan.FromSeconds(15))
                {
                    Console.WriteLine("Failed timing check - took " + delta.TotalSeconds + " seconds");
                    Assert.Fail();
                }

            }
            finally
            {
                // reset to defaults
                BrainCloudClient.Instance.SetPacketTimeoutsToDefault();
            }
        }

        public void MessageCacheGlobalError()
        {
            
        }

        [Test]
        public void TestMessageCache()
        {
            try
            {
                BrainCloudClient.Get().Initialize(ServerUrl + "unitTestFail", Secret, AppId, Version);
                BrainCloudClient.Get().EnableNetworkErrorMessageCaching(true);
                BrainCloudClient.Get().EnableLogging(true);
                BrainCloudClient.Get().SetPacketTimeouts(new List<int> { 1, 1, 1 });

                DateTime timeStart = DateTime.Now;
                TestResult tr = new TestResult();
                tr.SetTimeToWaitSecs(30);
                BrainCloudClient.Get().RegisterNetworkErrorCallback(tr.NetworkError);
                BrainCloudClient.Get().AuthenticationService.AuthenticateUniversal("abc", "abc", true, tr.ApiSuccess, tr.ApiError);
                tr.RunExpectFail(StatusCodes.CLIENT_NETWORK_ERROR, ReasonCodes.CLIENT_NETWORK_ERROR_TIMEOUT);

                BrainCloudClient.Get().RetryCachedMessages();
                tr.Reset();
                tr.RunExpectFail(StatusCodes.CLIENT_NETWORK_ERROR, ReasonCodes.CLIENT_NETWORK_ERROR_TIMEOUT);

                BrainCloudClient.Get().FlushCachedMessages(true);
                // unable to catch the api callback in this case using tr...

                //tr.Reset();
                //tr.RunExpectFail(StatusCodes.CLIENT_NETWORK_ERROR, ReasonCodes.CLIENT_NETWORK_ERROR_TIMEOUT);
            }
            finally
            {
                // reset to defaults
                BrainCloudClient.Get().SetPacketTimeoutsToDefault();
                BrainCloudClient.Get().EnableNetworkErrorMessageCaching(false);
                BrainCloudClient.Get().DeregisterNetworkErrorCallback();
            }
        }


        /*
        [Test]
        public void Test503()
        {
            try 
            {
                BrainCloudClient.Instance.Initialize("http://localhost:5432", _secret, _appId, _version);
                BrainCloudClient.Get ().EnableLogging(true);

                DateTime timeStart = DateTime.Now;
                TestResult tr = new TestResult();
                tr.SetTimeToWaitSecs(120);
                BrainCloudClient.Instance.AuthenticationService.AuthenticateUniversal("abc", "abc", true, tr.ApiSuccess, tr.ApiError);
                tr.RunExpectFail(StatusCodes.CLIENT_NETWORK_ERROR, ReasonCodes.CLIENT_NETWORK_ERROR_TIMEOUT);
                
                
                DateTime timeEnd = DateTime.Now;
                TimeSpan delta = timeEnd.Subtract(timeStart);
                if (delta < TimeSpan.FromSeconds (8) && delta > TimeSpan.FromSeconds(15))
                {
                    Console.WriteLine("Failed timing check - took " + delta.TotalSeconds + " seconds");
                    Assert.Fail ();
                }
                
            }
            finally
            {
                // reset to defaults
                BrainCloudClient.Get ().SetPacketTimeoutsToDefault();
            }
            
        }*/

        [Test]
        public void TestErrorCallback()
        {

            BrainCloudClient.Instance.Initialize(ServerUrl, Secret, AppId, Version);
            BrainCloudClient.Instance.EnableLogging(true);

            TestResult tr = new TestResult();
            BrainCloudClient.Instance.EntityService.CreateEntity("type", "", "", tr.ApiSuccess, tr.ApiError);
            tr.RunExpectFail(-1, -1);
            Console.Out.WriteLine(tr.m_statusMessage);
            Assert.True(tr.m_statusMessage.StartsWith("{"));

            BrainCloudClient.Instance.SetOldStyleStatusMessageErrorCallback(true);
            tr.Reset();

            BrainCloudClient.Instance.EntityService.CreateEntity("type", "", "", tr.ApiSuccess, tr.ApiError);
            tr.RunExpectFail(-1, -1);
            Console.Out.WriteLine(tr.m_statusMessage);
            Assert.False(tr.m_statusMessage.StartsWith("{"));

            // try now using 900 client timeout
            BrainCloudClient.Instance.Initialize("http://localhost:5432", Secret, AppId, Version);

            tr.Reset();
            BrainCloudClient.Instance.SetOldStyleStatusMessageErrorCallback(false);
            BrainCloudClient.Instance.EntityService.CreateEntity("type", "", "", tr.ApiSuccess, tr.ApiError);
            tr.RunExpectFail(-1, -1);
            Console.Out.WriteLine(tr.m_statusMessage);
            Assert.True(tr.m_statusMessage.StartsWith("{"));

            tr.Reset();
            BrainCloudClient.Instance.SetOldStyleStatusMessageErrorCallback(true);
            BrainCloudClient.Instance.EntityService.CreateEntity("type", "", "", tr.ApiSuccess, tr.ApiError);
            tr.RunExpectFail(-1, -1);
            Console.Out.WriteLine(tr.m_statusMessage);
            Assert.False(tr.m_statusMessage.StartsWith("{"));

            BrainCloudClient.Instance.SetOldStyleStatusMessageErrorCallback(false);
            BrainCloudClient.Instance.ResetCommunication();
        }

        [Test]
        public void TestGlobalErrorCallback()
        {
            BrainCloudClient.Instance.RegisterGlobalErrorCallback(GlobalErrorHandler);
            TestResult tr = new TestResult();

            BrainCloudClient.Instance.TimeService.ReadServerTime(tr.ApiSuccess, tr.ApiError);
            tr.RunExpectFail(StatusCodes.FORBIDDEN, ReasonCodes.NO_SESSION);

            Assert.AreEqual(_globalErrorCount, 1);

            BrainCloudClient.Instance.AuthenticationService.AuthenticateUniversal(
                GetUser(Users.UserA).Id,
                GetUser(Users.UserA).Password,
                false, tr.ApiSuccess, tr.ApiError);
            tr.Run();

            BrainCloudClient.Instance.TimeService.ReadServerTime(tr.ApiSuccess, tr.ApiError);
            tr.Run();

            BrainCloudClient.Instance.EntityService.UpdateEntity(
                "fakeId",
                "type",
                Helpers.CreateJsonPair("test", 2),
                null,
                -1,
                tr.ApiSuccess, tr.ApiError);
            tr.RunExpectFail(404, 40332);

            Assert.AreEqual(_globalErrorCount, 2);
        }

        [Test]
        public void TestGlobalErrorCallbackUsingWrapper()
        {
            BrainCloudClient.Instance.RegisterGlobalErrorCallback(GlobalErrorHandler);
            TestResult tr = new TestResult();

            BrainCloudWrapper.GetInstance().AuthenticateUniversal("", "zzz", true, tr.ApiSuccess, tr.ApiError, this);
            tr.RunExpectFail(StatusCodes.FORBIDDEN, ReasonCodes.TOKEN_DOES_NOT_MATCH_USER);

            Assert.AreEqual(_globalErrorCount, 1);
        }

        [Test]
        public void TestMessageBundleMarker()
        {
            TestResult tr = new TestResult();

            BrainCloudClient.Instance.AuthenticationService.AuthenticateUniversal(
                GetUser(Users.UserA).Id,
                GetUser(Users.UserA).Password,
                false, tr.ApiSuccess, tr.ApiError);
            BrainCloudClient.Instance.InsertEndOfMessageBundleMarker();

            BrainCloudClient.Instance.PlayerStatisticsService.ReadAllUserStats(
                tr.ApiSuccess, tr.ApiError);
            BrainCloudClient.Instance.InsertEndOfMessageBundleMarker();

            // to make sure it doesn't die on first message being marker
            BrainCloudClient.Instance.InsertEndOfMessageBundleMarker();

            BrainCloudClient.Instance.PlayerStatisticsService.ReadAllUserStats(
                tr.ApiSuccess, tr.ApiError);
            BrainCloudClient.Instance.PlayerStatisticsService.ReadAllUserStats(
                tr.ApiSuccess, tr.ApiError);
            
            // should result in three packets
            tr.Run();
            tr.Run();
            tr.Run();
        }

        [Test]
        public void TestAuthFirst()
        {
            TestResult tr = new TestResult();

            BrainCloudClient.Instance.PlayerStatisticsService.ReadAllUserStats(
                tr.ApiSuccess, tr.ApiError);

            BrainCloudClient.Instance.InsertEndOfMessageBundleMarker();

            BrainCloudClient.Instance.PlayerStatisticsService.ReadAllUserStats(
                tr.ApiSuccess, tr.ApiError);

            BrainCloudClient.Instance.AuthenticationService.AuthenticateUniversal(
                GetUser(Users.UserA).Id,
                GetUser(Users.UserA).Password,
                false, tr.ApiSuccess, tr.ApiError);



            // should result in two packets
            tr.RunExpectFail(403, ReasonCodes.NO_SESSION);
            tr.Run();
            tr.Run();
        }


        private void GlobalErrorHandler(int status, int reasonCode, string jsonError, object cbObject)
        {
            if (cbObject != null)
            {
                if (cbObject.GetType().ToString() == "BrainCloud.Internal.WrapperAuthCallbackObject")
                {
                    Console.WriteLine("GlobalErrorHandler received internal WrapperAuthCallbackObject object: " + cbObject.GetType().ToString());
                    throw new Exception("GlobalErrorHandler received internal WrapperAuthCallbackObject object");
                }
            }

            _globalErrorCount++;
            Console.Out.WriteLine("Global error: " + jsonError);
            Console.Out.WriteLine("Callback object: " + cbObject);
        }
    }

}