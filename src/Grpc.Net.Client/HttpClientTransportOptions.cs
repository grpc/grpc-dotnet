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
using System.Net.Http;

namespace Grpc.Net.Client
{
    /// <summary>
    /// A transport options class for configuring a channel to use <see cref="System.Net.Http.HttpClient"/> for transport.
    /// </summary>
    internal sealed class HttpClientTransportOptions : GrpcTransportOptions
    {
        /// <summary>
        /// Gets or sets the <see cref="System.Net.Http.HttpClient"/> used by the channel.
        /// </summary>
        public HttpClient? HttpClient { get; set; }

        internal override string Target => HttpClient?.BaseAddress?.Authority
            ?? throw new InvalidOperationException("Unable to create a gRPC channel because a target address couldn't be resolved from HttpClient. Ensure HttpClient.BaseAddress has a value set.");
    }
}
