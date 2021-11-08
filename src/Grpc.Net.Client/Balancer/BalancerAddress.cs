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

#if SUPPORT_LOAD_BALANCING
using System.Net;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Represents a balancer address.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public sealed class BalancerAddress
    {
        private BalancerAttributes? _attributes;

        /// <summary>
        /// Initializes a new instance of the <see cref="BalancerAddress"/> class with the specified <see cref="DnsEndPoint"/>.
        /// </summary>
        /// <param name="endPoint">The end point.</param>
        public BalancerAddress(DnsEndPoint endPoint)
        {
            EndPoint = endPoint;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BalancerAddress"/> class with the specified host and port.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="port">The port.</param>
        public BalancerAddress(string host, int port) : this(new DnsEndPoint(host, port))
        {
        }

        /// <summary>
        /// Gets the address <see cref="DnsEndPoint"/>.
        /// </summary>
        public DnsEndPoint EndPoint { get; }

        /// <summary>
        /// Gets the address attributes.
        /// </summary>
        public BalancerAttributes Attributes => _attributes ??= new BalancerAttributes();

        /// <summary>
        /// Returns a string that reprsents the address.
        /// </summary>
        public override string ToString()
        {
            return $"{EndPoint.Host}:{EndPoint.Port}";
        }
    }
}
#endif
