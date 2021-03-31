﻿#region Copyright notice and license

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
using System.Diagnostics;
using System.Net;
using Grpc.Core;
using Grpc.Net.Client.Configuration;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Represents the state for a channel. This is created from results returned by a <see cref="Resolver"/>.
    /// </summary>
    public sealed class ChannelState
    {
        [DebuggerStepThrough]
        internal ChannelState(Status status, IReadOnlyList<DnsEndPoint>? addresses, LoadBalancingConfig? loadBalancingConfig, BalancerAttributes attributes)
        {
            Addresses = addresses;
            LoadBalancingConfig = loadBalancingConfig;
            Status = status;
            Attributes = attributes;
        }

        /// <summary>
        /// Gets a collection of addresses. Will be <c>null</c> if <see cref="Status"/> has a non-OK value.
        /// </summary>
        public IReadOnlyList<DnsEndPoint>? Addresses { get; }

        /// <summary>
        /// Gets an optional service config.
        /// </summary>
        public LoadBalancingConfig? LoadBalancingConfig { get; }

        /// <summary>
        /// Gets the status.
        /// </summary>
        public Status Status { get; }

        /// <summary>
        /// Gets a collection of metadata attributes.
        /// </summary>
        public BalancerAttributes Attributes { get; }
    }

}
