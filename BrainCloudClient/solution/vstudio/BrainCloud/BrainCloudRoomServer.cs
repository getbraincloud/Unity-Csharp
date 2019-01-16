﻿//----------------------------------------------------
// brainCloud client source code
// Copyright 2016 bitHeads, inc.
//----------------------------------------------------

using System.Collections.Generic;
using BrainCloud.Internal;
namespace BrainCloud
{
    public class BrainCloudRoomServer
    {
        /// <summary>
        /// 
        /// </summary>
        internal BrainCloudRoomServer(BrainCloudClient in_client, RSComms in_comms)
        {
            m_clientRef = in_client;
            m_commsLayer = in_comms;
        }

        /// <summary>
        /// Requests the event server address
        /// </summary>
        public void Send(string in_message, Dictionary<string, object> in_options = null)
        {
            m_commsLayer.Send(in_message, in_options);
        }
        
        /// <summary>
        ///
        /// </summary>
        /// <param name="in_connectionType"></param>
        /// <param name="in_success"></param>
        /// <param name="in_failure"></param>
        /// <param name="cb_object"></param>
        public void EnableRS(eRSConnectionType in_connectionType = eRSConnectionType.WEBSOCKET, Dictionary<string, object> in_options = null, SuccessCallback in_success = null, FailureCallback in_failure = null, object cb_object = null)
        {
            m_commsLayer.EnableRS(in_connectionType, in_options, in_success, in_failure, cb_object);
        }

        /// <summary>
        /// Disables Room Service for this session.
        /// </summary>
        public void DisableRS()
        {
            m_commsLayer.DisableRS();
        }

        /// <summary>
        /// 
        /// </summary>
        public void RegisterRSCallback(RSCallback in_callback)
        {
            m_commsLayer.RegisterRSCallback(in_callback);
        }

        /// <summary>
        /// 
        /// </summary>
        public void DeregisterRSCallback()
        {
            m_commsLayer.DeregisterRSCallback();
        }

        #region private
        /// <summary>
        /// Reference to the brainCloud client object
        /// </summary>
        private BrainCloudClient m_clientRef;
        private RSComms m_commsLayer;
        #endregion

    }
}
