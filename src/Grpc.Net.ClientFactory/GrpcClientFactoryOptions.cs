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

using System;
using System.Collections.Generic;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace Grpc.Net.ClientFactory
{
    /// <summary>
    /// Options used to configure a gRPC client.
    /// </summary>
    public class GrpcClientFactoryOptions
    {
        /// <summary>
        /// The address to use when making gRPC calls.
        /// </summary>
        public Uri? Address { get; set; }

        /// <summary>
        /// Gets a list of operations used to configure a <see cref="GrpcChannelOptions"/>.
        /// </summary>
        public IList<Action<GrpcChannelOptions>> ChannelOptionsActions { get; } = new List<Action<GrpcChannelOptions>>();

        /// <summary>
        /// Gets a list of <see cref="Interceptor"/> instances used to configure a gRPC client pipeline.
        /// </summary>
        [Obsolete("Interceptors is obsolete and will be removed in a future release. Use InterceptorRegistrations instead.")]
        public IList<Interceptor> Interceptors { get; } = new List<Interceptor>();

        /// <summary>
        /// 
        /// </summary>
        public IList<InterceptorRegistration> InterceptorRegistrations { get; } = new List<InterceptorRegistration>();

        /// <summary>
        /// Gets or sets a delegate that will override how a client is created.
        /// </summary>
        public Func<CallInvoker, object>? Creator { get; set; }

        internal static CallInvoker BuildInterceptors(
            CallInvoker callInvoker,
            IServiceProvider serviceProvider,
            GrpcClientFactoryOptions clientFactoryOptions,
            InterceptorLifetime lifetime)
        {
            CallInvoker resolvedCallInvoker;
            if (clientFactoryOptions.InterceptorRegistrations.Count == 0)
            {
                resolvedCallInvoker = callInvoker;
            }
            else
            {
                var channelInterceptors = new List<Interceptor>();
                for (var i = 0; i < clientFactoryOptions.InterceptorRegistrations.Count; i++)
                {
                    var registration = clientFactoryOptions.InterceptorRegistrations[i];
                    if (registration.Lifetime == lifetime)
                    {
                        channelInterceptors.Add(registration.Creator(serviceProvider));
                    }
                }

                resolvedCallInvoker = callInvoker.Intercept(channelInterceptors.ToArray());
            }

            return resolvedCallInvoker;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class InterceptorRegistration
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="lifetime"></param>
        /// <param name="creator"></param>
        public InterceptorRegistration(InterceptorLifetime lifetime, Func<IServiceProvider, Interceptor> creator)
        {
            Lifetime = lifetime;
            Creator = creator;
        }

        /// <summary>
        /// 
        /// </summary>
        public InterceptorLifetime Lifetime { get; }

        /// <summary>
        /// 
        /// </summary>
        public Func<IServiceProvider, Interceptor> Creator { get; }
    }

    /// <summary>
    /// 
    /// </summary>
    public enum InterceptorLifetime
    {
        /// <summary>
        /// 
        /// </summary>
        Channel,
        /// <summary>
        /// 
        /// </summary>
        Client
    }
}
