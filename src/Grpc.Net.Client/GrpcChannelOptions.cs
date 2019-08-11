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

using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Compression;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client
{
    /// <summary>
    /// An options class for configuring a <see cref="GrpcChannel"/>.
    /// </summary>
    public sealed class GrpcChannelOptions
    {
        /// <summary>
        /// Gets or sets the credentials for the channel.
        /// </summary>
        public ChannelCredentials? Credentials { get; set; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be sent from the client.
        /// </summary>
        public int? SendMaxMessageSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be received by the client.
        /// </summary>
        public int? ReceiveMaxMessageSize { get; set; }

        /// <summary>
        /// Gets or sets a collection of compression providers.
        /// </summary>
        public IList<ICompressionProvider>? CompressionProviders { get; set; }

        /// <summary>
        /// Gets or sets the logger factory used by the channel.
        /// </summary>
        public ILoggerFactory? LoggerFactory { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="HttpClient"/> used by the channel.
        /// </summary>
        public HttpClient? HttpClient { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public GrpcChannelOptions()
        {
            ReceiveMaxMessageSize = GrpcChannel.DefaultReceiveMaxMessageSize;
        }
    }
}
