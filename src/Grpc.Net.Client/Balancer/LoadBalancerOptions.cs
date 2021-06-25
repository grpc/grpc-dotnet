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
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Options for creating a <see cref="LoadBalancer"/>.
    /// </summary>
    public sealed class LoadBalancerOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LoadBalancerOptions"/> class.
        /// </summary>
        /// <param name="controller">The controller.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="configuration">The load balancer configuration.</param>
        public LoadBalancerOptions(IChannelControlHelper controller, ILoggerFactory loggerFactory, IDictionary<string, object> configuration)
        {
            Controller = controller;
            LoggerFactory = loggerFactory;
            Configuration = configuration;
        }

        /// <summary>
        /// Gets the <see cref="IChannelControlHelper"/>.
        /// </summary>
        public IChannelControlHelper Controller { get; }

        /// <summary>
        /// Gets the logger factory.
        /// </summary>
        public ILoggerFactory LoggerFactory { get; }

        /// <summary>
        /// Gets the load balancer configuration.
        /// </summary>
        public IDictionary<string, object> Configuration { get; }
    }
}
#endif
