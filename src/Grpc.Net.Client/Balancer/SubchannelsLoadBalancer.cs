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
    /// <see cref="CreatePicker(List{Subchannel})"/> and returning a custom <see cref="SubchannelPicker"/>.
    /// </summary>
#if HAVE_LOAD_BALANCING
    public
#else
    internal
#endif
        abstract class SubchannelsLoadBalancer : LoadBalancer
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

            foreach (var address in state.Addresses)
            {
                var i = FindSubchannelByAddress(currentSubchannels, address);

                AddressSubchannel newSubConnection;
                if (i != null)
                {
                    newSubConnection = currentSubchannels[i.GetValueOrDefault()];
                    currentSubchannels.RemoveAt(i.GetValueOrDefault());
                }
                else
                {
                    var c = Controller.CreateSubchannel(new SubchannelOptions(new[] { address }));
                    c.StateChanged += UpdateSubchannelState;

                    newSubchannels.Add(c);
                    newSubConnection = new AddressSubchannel(c, address);
                }

                allUpdatedSubchannels.Add(newSubConnection);
            }

            // Any sub-connections still in this collection are no longer returned by the resolver.
            // This can all be removed.
            var removedSubConnections = currentSubchannels;

            if (removedSubConnections.Count == 0 && newSubchannels.Count == 0)
            {
                _logger.LogTrace("Connections unchanged.");
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
            var readySubchannels = _addressSubchannels
                .Select(s => s.Subchannel)
                .Where(s => s.State == ConnectivityState.Ready)
                .ToList();

            if (readySubchannels.Count == 0)
            {
                // No READY subchannels, determine aggregate state and error status
                var isConnecting = false;
                ConnectivityState? aggState = null;
                foreach (var subchannel in _addressSubchannels)
                {
                    var state = subchannel.Subchannel.State;

                    if (state == ConnectivityState.Connecting || state == ConnectivityState.Idle)
                    {
                        isConnecting = true;
                    }
                    if (aggState == null || aggState == ConnectivityState.TransientFailure)
                    {
                        aggState = state;
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

        private void UpdateSubchannelState(object? sender, SubchannelState state)
        {
            _logger.LogInformation("Updating subchannel state.");

            var subchannel = (Subchannel)sender!;

            var index = FindSubchannel(_addressSubchannels, subchannel);
            if (index == null)
            {
                _logger.LogInformation("Ignored state change because of unknown subchannel.");
                return;
            }

            UpdateBalancingState(state.Status);

            if (state.State == ConnectivityState.TransientFailure || state.State == ConnectivityState.Idle)
            {
                _logger.LogInformation($"Refreshing resolver because subchannel {subchannel} is in state {state.State}.");
                Controller.RefreshResolver();
            }
            if (state.State == ConnectivityState.Idle)
            {
                _logger.LogInformation($"Requesting connection for subchannel {subchannel} because it is in state {state.State}.");
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
        protected abstract SubchannelPicker CreatePicker(List<Subchannel> readySubchannels);

        private record AddressSubchannel(Subchannel Subchannel, DnsEndPoint Address);
    }

}
