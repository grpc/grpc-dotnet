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
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Model;
using Grpc.Net.Compression;
using Grpc.Shared.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grpc.Tests.Shared
{
    internal static class HttpContextServerCallContextHelper
    {
        public static HttpContextServerCallContext CreateServerCallContext(
            HttpContext? httpContext = null,
            List<ICompressionProvider>? compressionProviders = null,
            string? responseCompressionAlgorithm = null,
            CompressionLevel? responseCompressionLevel = null,
            int? maxSendMessageSize = null,
            int? maxReceiveMessageSize = null,
            ILogger? logger = null,
            bool initialize = true)
        {
            var options = CreateMethodOptions(
                compressionProviders,
                responseCompressionAlgorithm,
                responseCompressionLevel,
                maxSendMessageSize,
                maxReceiveMessageSize);

            var context = new HttpContextServerCallContext(
                httpContext ?? new DefaultHttpContext(),
                options,
                typeof(object),
                typeof(object),
                logger ?? NullLogger.Instance);
            if (initialize)
            {
                context.Initialize();
            }

            return context;
        }

        public static MethodOptions CreateMethodOptions(
            List<ICompressionProvider>? compressionProviders = null,
            string? responseCompressionAlgorithm = null,
            CompressionLevel? responseCompressionLevel = null,
            int? maxSendMessageSize = null,
            int? maxReceiveMessageSize = null,
            InterceptorCollection? interceptors = null)
        {
            var serviceOptions = new GrpcServiceOptions();
            serviceOptions.CompressionProviders = compressionProviders ?? new List<ICompressionProvider>();
            serviceOptions.Interceptors.AddRange(interceptors ?? new InterceptorCollection());
            serviceOptions.MaxSendMessageSize = maxSendMessageSize;
            serviceOptions.MaxReceiveMessageSize = maxReceiveMessageSize;
            serviceOptions.ResponseCompressionAlgorithm = responseCompressionAlgorithm;
            serviceOptions.ResponseCompressionLevel = responseCompressionLevel;

            return MethodOptions.Create(new[] { serviceOptions });
        }
    }
}
