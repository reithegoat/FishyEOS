﻿using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices.Unity;
using FishNet.Managing.Logging;
using UnityEngine;

namespace FishNet.Transporting.FishyEOSPlugin
{
    public abstract class CommonPeer
    {
        # region Private.

        /// <summary>Current ConnectionState.</summary>
        private LocalConnectionState _connectionState = LocalConnectionState.Stopped;

        #endregion

        #region Protected.

        /// <summary>Transport controlling this peer.</summary>
        protected FishyEOS Transport;

        #endregion

        /// <summary>Returns the current ConnectionState.</summary>
        /// <returns></returns>
        internal LocalConnectionState GetLocalConnectionState()
        {
            return _connectionState;
        }

        /// <summary>Sets a new connection state.</summary>
        /// <param name="connectionState"></param>
        protected virtual void SetLocalConnectionState(LocalConnectionState connectionState, bool server)
        {
            //If state hasn't changed.
            if (connectionState == _connectionState)
                return;

            _connectionState = connectionState;

            if (server)
                Transport.HandleServerConnectionState(new ServerConnectionStateArgs(connectionState, Transport.Index));
            else
                Transport.HandleClientConnectionState(new ClientConnectionStateArgs(connectionState, Transport.Index));
        }

        /// <summary>Initializes this for use.</summary>
        /// <param name="transport"></param>
        internal void Initialize(FishyEOS transport)
        {
            Transport = transport;
        }

        /// <summary>Clears a queue.</summary>
        /// <param name="queue"></param>
        internal void ClearQueue(ref Queue<LocalPacket> queue)
        {
            while (queue.Count > 0)
            {
                var lp = queue.Dequeue();
            }
        }

        /// <summary>Sends a message to remote user through EOS P2P Interface.</summary>
        internal Result Send(ProductUserId localUserId, ProductUserId remoteUserId, SocketId? socketId,
            byte channelId, ArraySegment<byte> segment)
        {
            if (GetLocalConnectionState() != LocalConnectionState.Started)
                return Result.InvalidState;

            var reliability =
                channelId == 0 ? PacketReliability.ReliableOrdered : PacketReliability.UnreliableUnordered;
            var allowDelayedDelivery = channelId == 0 ? true : false;

            var sendPacketOptions = new SendPacketOptions
            {
                LocalUserId = localUserId,
                RemoteUserId = remoteUserId,
                SocketId = socketId,
                Channel = channelId,
                Data = segment,
                Reliability = reliability,
                AllowDelayedDelivery = allowDelayedDelivery
            };
            var result = EOS.GetPlatformInterface().GetP2PInterface().SendPacket(ref sendPacketOptions);
            if (result != Result.Success)
                Debug.LogWarning(
                    $"Failed to send packet to {remoteUserId} with size {segment.Count} with error {result}");
            return result;
        }

        /// <summary>Returns a message from the EOS P2P Interface.</summary>
        protected bool Receive(ProductUserId localUserId, out ProductUserId remoteUserId, out ArraySegment<byte> data,
            out Channel channel)
        {
            remoteUserId = null;
            data = default;
            channel = Channel.Unreliable;

            var getNextReceivedPacketSizeOptions = new GetNextReceivedPacketSizeOptions
            {
                LocalUserId = localUserId,
            };
            var getPacketSizeResult = EOS.GetPlatformInterface().GetP2PInterface()
                .GetNextReceivedPacketSize(ref getNextReceivedPacketSizeOptions, out var packetSize);
            if (getPacketSizeResult == Result.NotFound)
            {
                return false; // this is fine, just no packets to read
            }

            if (getPacketSizeResult != Result.Success)
            {
                if (Transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError(
                        $"[{nameof(ClientPeer)}] GetNextReceivedPacketSize failed with error: {getPacketSizeResult}");
                return false;
            }

            var receivePacketOptions = new ReceivePacketOptions
            {
                LocalUserId = localUserId,
                MaxDataSizeBytes = packetSize,
            };
            data = new ArraySegment<byte>(new byte[packetSize]);
            var receivePacketResult = EOS.GetPlatformInterface().GetP2PInterface()
                .ReceivePacket(ref receivePacketOptions, out remoteUserId, out _, out var channelByte, data, out _);
            channel = (Channel)channelByte;
            if (receivePacketResult != Result.Success)
            {
                if (Transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError(
                        $"[{nameof(ClientPeer)}] ReceivePacket failed with error: {receivePacketResult}");
                return false;
            }

            return true;
        }

        /// <summary>Gets the number of packets incoming from the EOS P2P Interface.</summary>
        protected ulong GetIncomingPacketQueueCurrentPacketCount()
        {
            var getPacketQueueOptions = new GetPacketQueueInfoOptions();
            var getPacketQueueResult = EOS.GetPlatformInterface().GetP2PInterface()
                .GetPacketQueueInfo(ref getPacketQueueOptions, out var packetQueueInfo);
            if (getPacketQueueResult != Result.Success)
            {
                if (Transport.NetworkManager.CanLog(LoggingType.Error))
                    Debug.LogError($"[CommonSocket] Failed to get packet queue info with error {getPacketQueueResult}");
                return 0;
            }

            return packetQueueInfo.IncomingPacketQueueCurrentPacketCount;
        }
    }
}