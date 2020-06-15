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
    /// 
    /// </summary>
    public class GrpcClientBuilder
    {
        internal GrpcClientBuilder(IServiceProvider serviceProvider, string name)
        {
            Services = serviceProvider;
            Name = name;
            ChannelOptions = new GrpcChannelOptions();
            Interceptors = new List<Interceptor>();
        }

        /// <summary>
        /// 
        /// </summary>
        public IServiceProvider Services { get; }

        /// <summary>
        /// 
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 
        /// </summary>
        public IList<Interceptor> Interceptors { get; }

        /// <summary>
        /// 
        /// </summary>
        public GrpcChannelOptions ChannelOptions { get; }

        /// <summary>
        /// 
        /// </summary>
        public Func<CallInvoker, object>? Creator { get; set; }
    }
}
