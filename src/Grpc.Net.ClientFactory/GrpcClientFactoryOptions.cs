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
        [Obsolete("Interceptors collection is obsolete and will be removed in a future release. Use InterceptorRegistrations collection instead.")]
        public IList<Interceptor> Interceptors { get; } = new List<Interceptor>();

        /// <summary>
        /// Gets a list of <see cref="InterceptorRegistration"/> instances used to configure a gRPC client pipeline.
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
            InterceptorScope scope)
        {
            CallInvoker resolvedCallInvoker;
            if (clientFactoryOptions.InterceptorRegistrations.Count == 0)
            {
                resolvedCallInvoker = callInvoker;
            }
            else
            {
                List<Interceptor>? channelInterceptors = null;
                for (var i = 0; i < clientFactoryOptions.InterceptorRegistrations.Count; i++)
                {
                    var registration = clientFactoryOptions.InterceptorRegistrations[i];
                    if (registration.Scope == scope)
                    {
                        channelInterceptors ??= new List<Interceptor>();
                        channelInterceptors.Add(registration.Creator(serviceProvider));
                    }
                }

                resolvedCallInvoker = channelInterceptors != null
                    ? callInvoker.Intercept(channelInterceptors.ToArray())
                    : callInvoker;
            }

            return resolvedCallInvoker;
        }
    }
}
