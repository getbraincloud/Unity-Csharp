using NUnit.Core;
using NUnit.Framework;
using BrainCloud;

namespace BrainCloudTests
{
    [TestFixture]
    public class TestFriend : TestFixtureBase
    {
        [Test]
        public void TestFindPlayerByName()
        {
            TestResult tr = new TestResult();

            BrainCloudClient.Get().FriendService.FindPlayerByName(
                "search",
                10,
                tr.ApiSuccess, tr.ApiError);

            tr.Run();
        }

        [Test]
        public void TestUpdateSummaryFriendData()
        {
            TestResult tr = new TestResult();
            
            BrainCloudClient.Get().FriendService.UpdateSummaryFriendData(
                "{\"field\":\"value\"}",
                tr.ApiSuccess, tr.ApiError);
            
            tr.Run();
        }

    }
}