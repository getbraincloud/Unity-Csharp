﻿//----------------------------------------------------
// brainCloud client source code
// Copyright 2016 bitHeads, inc.
//----------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using JsonFx.Json;
using BrainCloud.Internal;

namespace BrainCloud
{
    public class BrainCloudPlaybackStream
    {

        private BrainCloudClient m_brainCloudClientRef;

        public BrainCloudPlaybackStream(BrainCloudClient in_brainCloudClientRef)
        {
            m_brainCloudClientRef = in_brainCloudClientRef;
        }

        /// <summary>
        /// Starts a stream
        /// </summary>
        /// <remarks>
        /// Service Name - PlaybackStream
        /// Service Operation - StartStream
        /// </remarks>
        /// <param name="in_targetPlayerId">
        /// The player to start a stream with
        /// </param>
        /// <param name="in_includeSharedData">
        /// Whether to include shared data in the stream
        /// </param>
        /// <param name="in_success">
        /// The success callback.
        /// </param>
        /// <param name="in_failure">
        /// The failure callback.
        /// </param>
        /// <param name="in_cbObject">
        /// The user object sent to the callback.
        /// </param>
        public void StartStream(
            string in_targetPlayerId,
            bool in_includeSharedData,
            SuccessCallback in_success = null,
            FailureCallback in_failure = null,
            object in_cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data[OperationParam.PlaybackStreamServiceTargetPlayerId.Value] = in_targetPlayerId;
            data[OperationParam.PlaybackStreamServiceIncludeSharedData.Value] = in_includeSharedData;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(in_success, in_failure, in_cbObject);
            ServerCall sc = new ServerCall(ServiceName.PlaybackStream, ServiceOperation.StartStream, data, callback);
            m_brainCloudClientRef.SendRequest(sc);
        }

        /// <summary>
        /// Reads a stream
        /// </summary>
        /// <remarks>
        /// Service Name - PlaybackStream
        /// Service Operation - ReadStream
        /// </remarks>
        /// <param name="in_playbackStreamId">
        /// Identifies the stream to read
        /// </param>
        /// <param name="in_success">
        /// The success callback.
        /// </param>
        /// <param name="in_failure">
        /// The failure callback.
        /// </param>
        /// <param name="in_cbObject">
        /// The user object sent to the callback.
        /// </param>
        public void ReadStream(
            string in_playbackStreamId,
            SuccessCallback in_success = null,
            FailureCallback in_failure = null,
            object in_cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data[OperationParam.PlaybackStreamServicePlaybackStreamId.Value] = in_playbackStreamId;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(in_success, in_failure, in_cbObject);
            ServerCall sc = new ServerCall(ServiceName.PlaybackStream, ServiceOperation.ReadStream, data, callback);
            m_brainCloudClientRef.SendRequest(sc);
        }

        /// <summary>
        /// Ends a stream
        /// </summary>
        /// <remarks>
        /// Service Name - PlaybackStream
        /// Service Operation - EndStream
        /// </remarks>
        /// <param name="in_playbackStreamId">
        /// Identifies the stream to read
        /// </param>
        /// <param name="in_success">
        /// The success callback.
        /// </param>
        /// <param name="in_failure">
        /// The failure callback.
        /// </param>
        /// <param name="in_cbObject">
        /// The user object sent to the callback.
        /// </param>
        public void EndStream(
            string in_playbackStreamId,
            SuccessCallback in_success = null,
            FailureCallback in_failure = null,
            object in_cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data[OperationParam.PlaybackStreamServicePlaybackStreamId.Value] = in_playbackStreamId;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(in_success, in_failure, in_cbObject);
            ServerCall sc = new ServerCall(ServiceName.PlaybackStream, ServiceOperation.EndStream, data, callback);
            m_brainCloudClientRef.SendRequest(sc);
        }

        /// <summary>
        /// Deletes a stream
        /// </summary>
        /// <remarks>
        /// Service Name - PlaybackStream
        /// Service Operation - DeleteStream
        /// </remarks>
        /// <param name="in_playbackStreamId">
        /// Identifies the stream to read
        /// </param>
        /// <param name="in_success">
        /// The success callback.
        /// </param>
        /// <param name="in_failure">
        /// The failure callback.
        /// </param>
        /// <param name="in_cbObject">
        /// The user object sent to the callback.
        /// </param>
        public void DeleteStream(
            string in_playbackStreamId,
            SuccessCallback in_success = null,
            FailureCallback in_failure = null,
            object in_cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data[OperationParam.PlaybackStreamServicePlaybackStreamId.Value] = in_playbackStreamId;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(in_success, in_failure, in_cbObject);
            ServerCall sc = new ServerCall(ServiceName.PlaybackStream, ServiceOperation.DeleteStream, data, callback);
            m_brainCloudClientRef.SendRequest(sc);
        }

        /// <summary>
        /// Adds a stream event
        /// </summary>
        /// <remarks>
        /// Service Name - PlaybackStream
        /// Service Operation - AddEvent
        /// </remarks>
        /// <param name="in_playbackStreamId">
        /// Identifies the stream to read
        /// </param>
        /// <param name="in_eventData">
        /// Describes the event
        /// </param>
        /// <param name="in_summary">
        /// Current summary data as of this event
        /// </param>
        /// <param name="in_success">
        /// The success callback.
        /// </param>
        /// <param name="in_failure">
        /// The failure callback.
        /// </param>
        /// <param name="in_cbObject">
        /// The user object sent to the callback.
        /// </param>
        public void AddEvent(
            string in_playbackStreamId,
            string in_eventData,
            string in_summary,
            SuccessCallback in_success = null,
            FailureCallback in_failure = null,
            object in_cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data[OperationParam.PlaybackStreamServicePlaybackStreamId.Value] = in_playbackStreamId;

            if (Util.IsOptionalParameterValid(in_eventData))
            {
                Dictionary<string, object> jsonEventData = JsonReader.Deserialize<Dictionary<string, object>> (in_eventData);
                data[OperationParam.PlaybackStreamServiceEventData.Value] = jsonEventData;
            }

            if (Util.IsOptionalParameterValid(in_summary))
            {
                Dictionary<string, object> jsonSummary = JsonReader.Deserialize<Dictionary<string, object>> (in_summary);
                data[OperationParam.PlaybackStreamServiceSummary.Value] = jsonSummary;
            }

            ServerCallback callback = BrainCloudClient.CreateServerCallback(in_success, in_failure, in_cbObject);
            ServerCall sc = new ServerCall(ServiceName.PlaybackStream, ServiceOperation.AddEvent, data, callback);
            m_brainCloudClientRef.SendRequest(sc);
        }

        /// <summary>
        /// Gets stream summaries for initiating player
        /// </summary>
        /// <remarks>
        /// Service Name - PlaybackStream
        /// Service Operation - GetStreamSummariesForInitiatingPlayer
        /// </remarks>
        /// <param name="in_initiatingPlayerId">
        /// The player that started the stream
        /// </param>
        /// <param name="in_success">
        /// The success callback.
        /// </param>
        /// <param name="in_failure">
        /// The failure callback.
        /// </param>
        /// <param name="in_cbObject">
        /// The user object sent to the callback.
        /// </param>
        public void GetStreamSummariesForInitiatingPlayer(
            string in_initiatingPlayerId,
            SuccessCallback in_success = null,
            FailureCallback in_failure = null,
            object in_cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data[OperationParam.PlaybackStreamServiceInitiatingPlayerId.Value] = in_initiatingPlayerId;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(in_success, in_failure, in_cbObject);
            ServerCall sc = new ServerCall(ServiceName.PlaybackStream, ServiceOperation.GetStreamSummariesForInitiatingPlayer, data, callback);
            m_brainCloudClientRef.SendRequest(sc);
        }

        /// <summary>
        /// Gets stream summaries for target player
        /// </summary>
        /// <remarks>
        /// Service Name - PlaybackStream
        /// Service Operation - GetStreamSummariesForTargetPlayer
        /// </remarks>
        /// <param name="in_targetPlayerId">
        /// The player that started the stream
        /// </param>
        /// <param name="in_success">
        /// The success callback.
        /// </param>
        /// <param name="in_failure">
        /// The failure callback.
        /// </param>
        /// <param name="in_cbObject">
        /// The user object sent to the callback.
        /// </param>
        public void GetStreamSummariesForTargetPlayer(
            string in_targetPlayerId,
            SuccessCallback in_success = null,
            FailureCallback in_failure = null,
            object in_cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data[OperationParam.PlaybackStreamServiceTargetPlayerId.Value] = in_targetPlayerId;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(in_success, in_failure, in_cbObject);
            ServerCall sc = new ServerCall(ServiceName.PlaybackStream, ServiceOperation.GetStreamSummariesForTargetPlayer, data, callback);
            m_brainCloudClientRef.SendRequest(sc);
        }
        
        /// <summary>
        /// Gets recent streams for initiating player
        /// </summary>
        /// <remarks>
        /// Service Name - PlaybackStream
        /// Service Operation - GetRecentSteamsForInitiatingPlayer
        /// </remarks>
        /// <param name="in_initiatingPlayerId">
        /// The player that started the stream
        /// </param>
        /// <param name="in_maxNumStreams">
        /// The player that started the stream
        /// </param>
        /// <param name="in_success">
        /// The success callback.
        /// </param>
        /// <param name="in_failure">
        /// The failure callback.
        /// </param>
        /// <param name="in_cbObject">
        /// The user object sent to the callback.
        /// </param>
        public void GetRecentStreamsForInitiatingPlayer(
            string in_initiatingPlayerId,
            int in_maxNumStreams,
            SuccessCallback in_success = null,
            FailureCallback in_failure = null,
            object in_cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data[OperationParam.PlaybackStreamServiceInitiatingPlayerId.Value] = in_initiatingPlayerId;
            data[OperationParam.PlaybackStreamServiceMaxNumberOfStreams.Value] = in_maxNumStreams;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(in_success, in_failure, in_cbObject);
            ServerCall sc = new ServerCall(ServiceName.PlaybackStream, ServiceOperation.GetRecentStreamsForInitiatingPlayer, data, callback);
            m_brainCloudClientRef.SendRequest(sc);
        }
        
        /// <summary>
        /// Gets recent streams for target player
        /// </summary>
        /// <remarks>
        /// Service Name - PlaybackStream
        /// Service Operation - GetRecentSteamsForTargetPlayer
        /// </remarks>
        /// <param name="in_targetPlayerId">
        /// The player that started the stream
        /// </param>
        /// <param name="in_maxNumStreams">
        /// The player that started the stream
        /// </param>
        /// <param name="in_success">
        /// The success callback.
        /// </param>
        /// <param name="in_failure">
        /// The failure callback.
        /// </param>
        /// <param name="in_cbObject">
        /// The user object sent to the callback.
        /// </param>
        public void GetRecentStreamsForTargetPlayer(
            string in_targetPlayerId,
            int in_maxNumStreams,
            SuccessCallback in_success = null,
            FailureCallback in_failure = null,
            object in_cbObject = null)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data[OperationParam.PlaybackStreamServiceTargetPlayerId.Value] = in_targetPlayerId;
            data[OperationParam.PlaybackStreamServiceMaxNumberOfStreams.Value] = in_maxNumStreams;

            ServerCallback callback = BrainCloudClient.CreateServerCallback(in_success, in_failure, in_cbObject);
            ServerCall sc = new ServerCall(ServiceName.PlaybackStream, ServiceOperation.GetRecentStreamsForTargetPlayer, data, callback);
            m_brainCloudClientRef.SendRequest(sc);
        }
    }
}
