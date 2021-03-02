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
using Grpc.Net.Client;

namespace Grpc.Shared
{
    internal static class HttpHandlerFactory
    {
        public static HttpMessageHandler CreatePrimaryHandler()
        {
#if NET5_0
            // If we're in .NET 5 and SocketsHttpHandler is supported (it's not in Blazor WebAssembly)
            // then create SocketsHttpHandler with EnableMultipleHttp2Connections set to true. That will
            // allow a gRPC channel to create new connections if the maximum allow concurrency is exceeded.
            if (SocketsHttpHandler.IsSupported)
            {
                return new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true
                };
            }
#endif

#if !NETSTANDARD2_0
            return new HttpClientHandler();
#else
            var message =
                $"gRPC requires extra configuration to successfully make RPC calls on older platforms such " +
                $"as .NET Framework. An HTTP provider must be specified using {nameof(GrpcChannelOptions)}.{nameof(GrpcChannelOptions.HttpHandler)} or " +
                $"{nameof(GrpcChannelOptions)}.{nameof(GrpcChannelOptions.HttpClient)}. The configured HTTP provider must either support HTTP/2 or " +
                $"be configured to use gRPC-Web. See https://aka.ms/pzkMXDs for details.";
            throw new PlatformNotSupportedException(message);
#endif
        }

#if NET5_0
        public static HttpMessageHandler EnsureTelemetryHandler(HttpMessageHandler handler)
        {
            // HttpClientHandler has an internal handler that sets request telemetry header.
            // If the handler is SocketsHttpHandler then we know that the header will never be set
            // so wrap with a handler that is responsible for setting the telemetry header.
            if (HasHttpHandlerType(handler, "System.Net.Http.SocketsHttpHandler"))
            {
                // Double check telemetry handler hasn't already been added by something else
                // like the client factory when it created the primary handler.
                if (!HasHttpHandlerType(handler, typeof(TelemetryHeaderHandler).FullName!))
                {
                    return new TelemetryHeaderHandler(handler);
                }
            }

            return handler;
        }
#endif

        public static bool HasHttpHandlerType(HttpMessageHandler handler, string handlerTypeName)
        {
            if (handler?.GetType().FullName == handlerTypeName)
            {
                return true;
            }

            HttpMessageHandler? currentHandler = handler;
            DelegatingHandler? delegatingHandler;
            while ((delegatingHandler = currentHandler as DelegatingHandler) != null)
            {
                currentHandler = delegatingHandler.InnerHandler;

                if (currentHandler?.GetType().FullName == handlerTypeName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
