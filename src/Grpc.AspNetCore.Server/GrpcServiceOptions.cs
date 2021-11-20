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

using System.IO.Compression;
using Grpc.Net.Compression;

namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// Options used to configure service instances.
    /// </summary>
    public class GrpcServiceOptions
    {
        internal IList<ICompressionProvider>? _compressionProviders;
        internal bool _maxReceiveMessageSizeConfigured;
        internal int? _maxReceiveMessageSize;
        internal bool _maxSendMessageSizeConfigured;
        internal int? _maxSendMessageSize;

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be sent from the server.
        /// Attempting to send a message that exceeds the configured maximum message size results in an exception.
        /// <para>
        /// A <c>null</c> value removes the maximum message size limit. Defaults to <c>null</c>.
        /// </para>
        /// </summary>
        public int? MaxSendMessageSize
        {
            get => _maxSendMessageSize;
            set
            {
                _maxSendMessageSize = value;
                _maxSendMessageSizeConfigured = true;
            }
        }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be received by the server.
        /// If the server receives a message that exceeds this limit, it throws an exception.
        /// <para>
        /// A <c>null</c> value removes the maximum message size limit. Defaults to 4,194,304 (4 MB).
        /// </para>
        /// </summary>
        public int? MaxReceiveMessageSize
        {
            get => _maxReceiveMessageSize;
            set
            {
                _maxReceiveMessageSize = value;
                _maxReceiveMessageSizeConfigured = true;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether detailed error messages are sent to the peer.
        /// Detailed error messages include details from exceptions thrown on the server.
        /// </summary>
        public bool? EnableDetailedErrors { get; set; }

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
        public IList<ICompressionProvider> CompressionProviders
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
        /// Gets or sets a value indicating whether gRPC should ignore calls to unknown services and methods.
        /// If set to <c>true</c>, calls to unknown services and methods won't return an 'UNIMPLEMENTED' status,
        /// and the request will pass to the next registered middleware in ASP.NET Core.
        /// </summary>
        public bool? IgnoreUnknownServices { get; set; }

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
