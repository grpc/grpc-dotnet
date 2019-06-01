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
using System.IO.Compression;
using Grpc.AspNetCore.Server.Compression;

namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// Options used to configure service instances.
    /// </summary>
    public class GrpcServiceOptions
    {
        internal List<ICompressionProvider>? _compressionProviders;

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be sent from the server.
        /// </summary>
        public int? SendMaxMessageSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be received by the server.
        /// </summary>
        public int? ReceiveMaxMessageSize { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether detailed error messages are sent to the peer.
        /// Detailed error messages include details from exceptions thrown on the server.
        /// </summary>
        public bool? EnableDetailedErrors { get; set; } = null;

        /// <summary>
        /// Gets or sets the compression algorithm used to compress messages sent from the server.
        /// The request grpc-accept-encoding header value must contain this algorithm for it to
        /// be used.
        /// </summary>
        public string? ResponseCompressionAlgorithm { get; set; }

        /// <summary>
        /// Gets or sets the compression level used to compress messages sent from the server.
        /// The compression level will be passed to the compression provider.
        /// </summary>
        public CompressionLevel? ResponseCompressionLevel { get; set; }

        /// <summary>
        /// Gets or sets the list of compression providers used to compress and decompress gRPC messages.
        /// </summary>
        public List<ICompressionProvider> CompressionProviders
        {
            get
            {
                if (_compressionProviders == null)
                {
                    _compressionProviders = new List<ICompressionProvider>();
                }
                return _compressionProviders;
            }
            set => _compressionProviders = value;
        }

        /// <summary>
        /// Get a collection of interceptors to be executed with every call. Interceptors are executed in order.
        /// </summary>
        public InterceptorCollection Interceptors { get; } = new InterceptorCollection();
    }

    /// <summary>
    /// Options used to configure the specified service type instances. These options override globally set options.
    /// </summary>
    /// <typeparam name="TService">The service type to configure.</typeparam>
    public class GrpcServiceOptions<TService> : GrpcServiceOptions where TService : class
    {
    }
}
