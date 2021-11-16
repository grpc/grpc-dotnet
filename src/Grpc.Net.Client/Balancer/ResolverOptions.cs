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
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Options for creating a resolver.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public sealed class ResolverOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResolverOptions"/> class.
        /// </summary>
        internal ResolverOptions(Uri address, int defaultPort, bool disableServiceConfig, ILoggerFactory loggerFactory)
        {
            Address = address;
            DefaultPort = defaultPort;
            DisableServiceConfig = disableServiceConfig;
            LoggerFactory = loggerFactory;
        }

        /// <summary>
        /// Gets the address.
        /// </summary>
        public Uri Address { get; }

        /// <summary>
        /// Gets the default port. This port is used when the resolver address doesn't specify a port.
        /// </summary>
        public int DefaultPort { get; }

        /// <summary>
        /// Gets a flag indicating whether the resolver should disable resolving a service config.
        /// </summary>
        public bool DisableServiceConfig { get; }

        /// <summary>
        /// Gets the logger factory.
        /// </summary>
        public ILoggerFactory LoggerFactory { get; }
    }
}
#endif
