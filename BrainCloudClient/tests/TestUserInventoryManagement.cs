using NUnit.Core;
using NUnit.Framework;
using BrainCloud;
using System;
using System.Collections.Generic;

namespace BrainCloudTests
{
    [TestFixture]
    public class TestUserInventoryManagement : TestFixtureBase
    {

        List<object> testItems = new List<object>();

        [Test]
        public void AwardUserItem()
        {
            TestResult tr = new TestResult(_bc);
            _bc.UserInventoryManagementService.AwardUserItem("sword001", 5, true,
                tr.ApiSuccess, tr.ApiError);
            tr.Run();

            //get a list of the items 
            var data = tr.m_response["data"] as Dictionary<string, object>;
            var items = data["items"] as Dictionary<string, object>;
            foreach (KeyValuePair<string, object> item in items) {
                 testItems.Add(item.Key);
            }
        }

        [Test]
        public void DropUserItem()
        {
            TestResult tr = new TestResult(_bc);
            _bc.UserInventoryManagementService.DropUserItem(testItems[0] as string, 1, true,
                tr.ApiSuccess, tr.ApiError);
            tr.Run();
        }

        [Test]
        public void GetUserInventory()
        {
            Dictionary<string, object> criteria = new Dictionary<string, object>();
            criteria.Add("itemData.bonus", 1);
            TestResult tr = new TestResult(_bc);
            _bc.UserInventoryManagementService.GetUserInventory(criteria, true,
                tr.ApiSuccess, tr.ApiError);
            tr.Run();
        }

        [Test]
        public void GetUserInventoryPage()
        {
            Dictionary<string, object> criteria = new Dictionary<string, object>();
            Dictionary<string, object> pagination = new Dictionary<string, object>();
            Dictionary<string, object> searchCriteria = new Dictionary<string, object>();
            Dictionary<string, object> sortCriteria = new Dictionary<string, object>();
            pagination.Add("rowsPerPage", 50);
            pagination.Add("pageNumber", 1);
            searchCriteria.Add("category", "sword");
            sortCriteria.Add("createdAt", 1);
            sortCriteria.Add("updatedAt", -1);
            criteria.Add("itemData.pagination", pagination);
            criteria.Add("itemData.searchCriteria", searchCriteria);
            criteria.Add("itemData.sortCriteria", sortCriteria);
            TestResult tr = new TestResult(_bc);
            _bc.UserInventoryManagementService.GetUserInventoryPage(criteria, true,
                tr.ApiSuccess, tr.ApiError);
            tr.Run();
        }

        [Test]
        public void GetUserInventoryPageOffset()
        {
            string context = "eyJzZWFyY2hDcml0ZXJpYSI6eyJnYW1lSWQiOiIyMDAwMSIsInBsYXllcklkIjoiNmVhYWU4M2EtYjZkMy00NTM5LWExZjAtZTIxMmMzYjUzMGIwIiwiZ2lmdGVkVG8iOm51bGx9LCJzb3J0Q3JpdGVyaWEiOnt9LCJwYWdpbmF0aW9uIjp7InJvd3NQZXJQYWdlIjoxMDAsInBhZ2VOdW1iZXIiOm51bGx9LCJvcHRpb25zIjpudWxsfQ";
            TestResult tr = new TestResult(_bc);
            _bc.UserInventoryManagementService.GetUserInventoryPageOffset(context, 1, true,
                tr.ApiSuccess, tr.ApiError);
            tr.Run();
        }

        [Test]
        public void GetUserItem()
        {
            TestResult tr = new TestResult(_bc);
            _bc.UserInventoryManagementService.GetUserItem(testItems[1] as string, true,
                tr.ApiSuccess, tr.ApiError);
            tr.Run();
        }

        [Test]
        public void GiveUserItemTo()
        {
            TestResult tr = new TestResult(_bc);
            _bc.UserInventoryManagementService.GiveUserItemTo(GetUser(Users.UserB).ProfileId, testItems[2] as string, 1, true,
                tr.ApiSuccess, tr.ApiError);
            tr.Run();
        }

        [Test]
        public void PurchaseUserItem()
        {
            TestResult tr = new TestResult(_bc);
            _bc.UserInventoryManagementService.PurchaseUserItem("sword001", 1, null, true,
                tr.ApiSuccess, tr.ApiError);
            tr.Run();
        }
        
        [Test]
        public void ReceiveUserItemFrom()
        {
            TestResult tr2 = new TestResult(_bc);
            _bc.UserInventoryManagementService.ReceiveUserItemFrom(GetUser(Users.UserA).ProfileId, testItems[3] as string,
                tr2.ApiSuccess, tr2.ApiError);
            tr2.RunExpectFail(StatusCodes.BAD_REQUEST, 40660);
        }
        
        [Test]
        public void SellUserItem()
        {
            TestResult tr2 = new TestResult(_bc);
            _bc.UserInventoryManagementService.SellUserItem(testItems[3] as string, 1, 1, null, true,
                tr2.ApiSuccess, tr2.ApiError);
            tr2.Run();
        }

        [Test]
        public void UpdateUserItemData()
        {
            Dictionary<string, object> newItemData = new Dictionary<string,object>();
            TestResult tr2 = new TestResult(_bc);
            _bc.UserInventoryManagementService.UpdateUserItemData(testItems[4] as string, 1, newItemData,
                tr2.ApiSuccess, tr2.ApiError);
            tr2.Run();
        }

                [Test]
        public void UseUserItem()
        {
            Dictionary<string, object> newItemData = new Dictionary<string,object>();
            TestResult tr2 = new TestResult(_bc);
            _bc.UserInventoryManagementService.UseUserItem(testItems[4] as string, 2, newItemData, true,
                tr2.ApiSuccess, tr2.ApiError);
            tr2.Run();
        }
    }
}