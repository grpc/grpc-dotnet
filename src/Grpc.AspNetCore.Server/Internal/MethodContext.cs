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
using System.IO.Compression;
using Grpc.Net.Compression;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class MethodContext
    {
        public Type RequestType { get; }
        public Type ResponseType { get; }
        public Dictionary<string, ICompressionProvider> CompressionProviders { get; }
        public InterceptorCollection Interceptors { get; }
        // Fast check for whether the service has any interceptors
        public bool HasInterceptors { get; }
        public int? MaxSendMessageSize { get; }
        public int? MaxReceiveMessageSize { get; }
        public bool? EnableDetailedErrors { get; }
        public string? ResponseCompressionAlgorithm { get; }
        public CompressionLevel? ResponseCompressionLevel { get; }

        public MethodContext(
            Type requestType,
            Type responseType,
            Dictionary<string, ICompressionProvider> compressionProviders,
            InterceptorCollection interceptors,
            int? maxSendMessageSize,
            int? maxReceiveMessageSize,
            bool? enableDetailedErrors,
            string? responseCompressionAlgorithm,
            CompressionLevel? responseCompressionLevel)
        {
            RequestType = requestType;
            ResponseType = responseType;
            CompressionProviders = compressionProviders;
            Interceptors = interceptors;
            HasInterceptors = interceptors.Count > 0;
            MaxSendMessageSize = maxSendMessageSize;
            MaxReceiveMessageSize = maxReceiveMessageSize;
            EnableDetailedErrors = enableDetailedErrors;
            ResponseCompressionAlgorithm = responseCompressionAlgorithm;
            ResponseCompressionLevel = responseCompressionLevel;

            if (ResponseCompressionAlgorithm != null)
            {
                if (!CompressionProviders.TryGetValue(ResponseCompressionAlgorithm, out var _))
                {
                    throw new InvalidOperationException($"The configured response compression algorithm '{ResponseCompressionAlgorithm}' does not have a matching compression provider.");
                }
            }
        }
    }
}
