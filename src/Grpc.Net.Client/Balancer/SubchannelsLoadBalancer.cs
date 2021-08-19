#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

#if SUPPORT_LOAD_BALANCING
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Grpc.Core;
using Grpc.Net.Client.Balancer.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// An abstract <see cref="LoadBalancer"/> that manages creating <see cref="Subchannel"/> instances
    /// from addresses. It is designed to make it easy to implement a custom picking policy by overriding
    /// <see cref="CreatePicker(IReadOnlyList{Subchannel})"/> and returning a custom <see cref="SubchannelPicker"/>.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public abstract class SubchannelsLoadBalancer : LoadBalancer
    {
        /// <summary>
        /// Gets the controller.
        /// </summary>
        protected IChannelControlHelper Controller { get; }

        /// <summary>
        /// Gets the connectivity state.
        /// </summary>
        protected ConnectivityState State { get; private set; }

        private readonly List<AddressSubchannel> _addressSubchannels;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SubchannelsLoadBalancer"/> class.
        /// </summary>
        /// <param name="controller">The controller.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        protected SubchannelsLoadBalancer(IChannelControlHelper controller, ILoggerFactory loggerFactory)
        {
            _addressSubchannels = new List<AddressSubchannel>();
            Controller = controller;
            _logger = loggerFactory.CreateLogger(GetType());
        }

        private void ResolverError(Status status)
        {
            // If balancer doesn't have a ready subchannel then remove any current subchannels
            // and update channel state with resolver error.
            switch (State)
            {
                case ConnectivityState.Idle:
                case ConnectivityState.Connecting:
                case ConnectivityState.TransientFailure:
                    foreach (var addressSubchannel in _addressSubchannels)
                    {
                        RemoveSubchannel(addressSubchannel.Subchannel);
                    }
                    Controller.UpdateState(new BalancerState(ConnectivityState.TransientFailure, new ErrorPicker(status)));
                    break;
            }
        }

        private int? FindSubchannelByAddress(List<AddressSubchannel> addressSubchannels, DnsEndPoint endPoint)
        {
            for (var i = 0; i < addressSubchannels.Count; i++)
            {
                var s = addressSubchannels[i];
                if (Equals(s.Address, endPoint))
                {
                    return i;
                }
            }

            return null;
        }

        private int? FindSubchannel(List<AddressSubchannel> addressSubchannels, Subchannel subchannel)
        {
            for (var i = 0; i < addressSubchannels.Count; i++)
            {
                var s = addressSubchannels[i];
                if (Equals(s.Subchannel, subchannel))
                {
                    return i;
                }
            }

            return null;
        }

        /// <inheritdoc />
        public override void UpdateChannelState(ChannelState state)
        {
            if (state.Status.StatusCode != StatusCode.OK)
            {
                ResolverError(state.Status);
                return;
            }
            if (state.Addresses == null || state.Addresses.Count == 0)
            {
                ResolverError(new Status(StatusCode.Unavailable, "Resolver returned no addresses."));
                return;
            }

            var allUpdatedSubchannels = new List<AddressSubchannel>();
            var newSubchannels = new List<Subchannel>();
            var currentSubchannels = _addressSubchannels.ToList();

            // The state's addresses is the new authoritative list of addresses.
            // However, we want to keep existing subchannels when possible.
            foreach (var address in state.Addresses)
            {
                // Check existing subchannels for a match.
                var i = FindSubchannelByAddress(currentSubchannels, address);

                AddressSubchannel newOrCurrentSubConnection;
                if (i != null)
                {
                    // There is a match so take current subchannel.
                    newOrCurrentSubConnection = currentSubchannels[i.GetValueOrDefault()];

                    // Remove from current collection because any subchannels
                    // remaining in this collection at the end will be disposed.
                    currentSubchannels.RemoveAt(i.GetValueOrDefault());
                }
                else
                {
                    // No match so create a new subchannel.
                    var c = Controller.CreateSubchannel(new SubchannelOptions(new[] { address }));
                    c.OnStateChanged(s => UpdateSubchannelState(c, s));

                    newSubchannels.Add(c);
                    newOrCurrentSubConnection = new AddressSubchannel(c, address);
                }

                allUpdatedSubchannels.Add(newOrCurrentSubConnection);
            }

            // Any sub-connections still in this collection are no longer returned by the resolver.
            // This can all be removed.
            var removedSubConnections = currentSubchannels;

            if (removedSubConnections.Count == 0 && newSubchannels.Count == 0)
            {
                SubchannelsLoadBalancerLog.ConnectionsUnchanged(_logger);
                return;
            }

            foreach (var removedSubConnection in removedSubConnections)
            {
                RemoveSubchannel(removedSubConnection.Subchannel);
            }

            _addressSubchannels.Clear();
            _addressSubchannels.AddRange(allUpdatedSubchannels);

            // Start new connections after collection on balancer has been updated.
            foreach (var c in newSubchannels)
            {
                c.RequestConnection();
            }

            UpdateBalancingState(state.Status);
        }

        private void UpdateBalancingState(Status status)
        {
            var readySubchannels = new List<Subchannel>();
            for (var i = 0; i < _addressSubchannels.Count; i++)
            {
                var addressSubchannel = _addressSubchannels[i];
                if (addressSubchannel.Subchannel.State == ConnectivityState.Ready)
                {
                    readySubchannels.Add(addressSubchannel.Subchannel);
                }
            }

            if (readySubchannels.Count == 0)
            {
                // No READY subchannels, determine aggregate state and error status
                var isConnecting = false;
                foreach (var subchannel in _addressSubchannels)
                {
                    var state = subchannel.Subchannel.State;

                    if (state == ConnectivityState.Connecting || state == ConnectivityState.Idle)
                    {
                        isConnecting = true;
                        break;
                    }
                }

                if (isConnecting)
                {
                    UpdateChannelState(ConnectivityState.Connecting, EmptyPicker.Instance);
                }
                else
                {
                    // Only care about status if it is non-OK.
                    // Pass it to the picker so that it is reported to the caller on pick.
                    var errorStatus = status.StatusCode != StatusCode.OK
                        ? status
                        : new Status(StatusCode.Internal, "Unknown error.");

                    UpdateChannelState(ConnectivityState.TransientFailure, new ErrorPicker(errorStatus));
                }
            }
            else
            {
                UpdateChannelState(ConnectivityState.Ready, CreatePicker(readySubchannels));
            }
        }

        private void UpdateChannelState(ConnectivityState state, SubchannelPicker subchannelPicker)
        {
            State = state;
            Controller.UpdateState(new BalancerState(state, subchannelPicker));
        }

        private void UpdateSubchannelState(Subchannel subchannel, SubchannelState state)
        {
            var index = FindSubchannel(_addressSubchannels, subchannel);
            if (index == null)
            {
                SubchannelsLoadBalancerLog.IgnoredSubchannelStateChange(_logger, subchannel.Id);
                return;
            }

            SubchannelsLoadBalancerLog.ProcessingSubchannelStateChanged(_logger, subchannel.Id, state.State, state.Status);

            UpdateBalancingState(state.Status);

            if (state.State == ConnectivityState.TransientFailure || state.State == ConnectivityState.Idle)
            {
                SubchannelsLoadBalancerLog.RefreshingResolverForSubchannel(_logger, subchannel.Id, state.State);
                Controller.RefreshResolver();
            }
            if (state.State == ConnectivityState.Idle)
            {
                SubchannelsLoadBalancerLog.RequestingConnectionForSubchannel(_logger, subchannel.Id, state.State);
                subchannel.RequestConnection();
            }
        }

        private void RemoveSubchannel(Subchannel subchannel)
        {
            subchannel.Dispose();
        }

        /// <inheritdoc />
        public override void RequestConnection()
        {
            foreach (var addressSubchannel in _addressSubchannels)
            {
                addressSubchannel.Subchannel.RequestConnection();
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            foreach (var addressSubchannel in _addressSubchannels)
            {
                RemoveSubchannel(addressSubchannel.Subchannel);
            }
            _addressSubchannels.Clear();
        }

        /// <summary>
        /// Creates a <see cref="SubchannelPicker"/> for the specified <see cref="Subchannel"/> instances.
        /// This method can be overriden to return new a <see cref="SubchannelPicker"/> implementation
        /// with custom load balancing logic.
        /// </summary>
        /// <param name="readySubchannels">A collection of ready subchannels.</param>
        /// <returns>A subchannel picker.</returns>
        protected abstract SubchannelPicker CreatePicker(IReadOnlyList<Subchannel> readySubchannels);

        private record AddressSubchannel(Subchannel Subchannel, DnsEndPoint Address);
    }

    internal static class SubchannelsLoadBalancerLog
    {
        private static readonly Action<ILogger, int, ConnectivityState, string, Exception?> _processingSubchannelStateChanged =
            LoggerMessage.Define<int, ConnectivityState, string>(LogLevel.Trace, new EventId(1, "ProcessingSubchannelStateChanged"), "Processing subchannel id '{SubchannelId}' state changed to {State}. Detail: '{Detail}'.");

        private static readonly Action<ILogger, int, Exception?> _ignoredSubchannelStateChange =
            LoggerMessage.Define<int>(LogLevel.Trace, new EventId(2, "IgnoredSubchannelStateChange"), "Ignored state change because of unknown subchannel id '{SubchannelId}'.");

        private static readonly Action<ILogger, Exception?> _connectionsUnchanged =
            LoggerMessage.Define(LogLevel.Trace, new EventId(3, "ConnectionsUnchanged"), "Connections unchanged.");

        private static readonly Action<ILogger, int, ConnectivityState, Exception?> _refreshingResolverForSubchannel =
            LoggerMessage.Define<int, ConnectivityState>(LogLevel.Trace, new EventId(4, "RefreshingResolverForSubchannel"), "Refreshing resolver because subchannel id '{SubchannelId}' is in state {State}.");

        private static readonly Action<ILogger, int, ConnectivityState, Exception?> _requestingConnectionForSubchannel =
            LoggerMessage.Define<int, ConnectivityState>(LogLevel.Trace, new EventId(5, "RequestingConnectionForSubchannel"), "Requesting connection for subchannel id '{SubchannelId}' because it is in state {State}.");

        public static void ProcessingSubchannelStateChanged(ILogger logger, int subchannelId, ConnectivityState state, Status status)
        {
            _processingSubchannelStateChanged(logger, subchannelId, state, status.Detail, status.DebugException);
        }

        public static void IgnoredSubchannelStateChange(ILogger logger, int subchannelId)
        {
            _ignoredSubchannelStateChange(logger, subchannelId, null);
        }

        public static void ConnectionsUnchanged(ILogger logger)
        {
            _connectionsUnchanged(logger, null);
        }

        public static void RefreshingResolverForSubchannel(ILogger logger, int subchannelId, ConnectivityState state)
        {
            _refreshingResolverForSubchannel(logger, subchannelId, state, null);
        }

        public static void RequestingConnectionForSubchannel(ILogger logger, int subchannelId, ConnectivityState state)
        {
            _requestingConnectionForSubchannel(logger, subchannelId, state, null);
        }
    }
}
#endif
