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
using Grpc.Net.Client.Internal;

namespace Grpc.Shared
{
    internal static class HttpHandlerFactory
    {
        public static HttpMessageHandler CreatePrimaryHandler()
        {
#if NET5_0_OR_GREATER
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
                $"gRPC requires extra configuration on .NET implementations that don't support gRPC over HTTP/2. " +
                $"An HTTP provider must be specified using {nameof(GrpcChannelOptions)}.{nameof(GrpcChannelOptions.HttpHandler)}." +
                $"The configured HTTP provider must either support HTTP/2 or be configured to use gRPC-Web. " +
                $"See https://aka.ms/aspnet/grpc/netstandard for details.";
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
            return GetHttpHandlerType(handler, handlerTypeName) != null;
        }

        public static HttpMessageHandler? GetHttpHandlerType(HttpMessageHandler handler, string handlerTypeName)
        {
            if (handler?.GetType().FullName == handlerTypeName)
            {
                return handler;
            }

            HttpMessageHandler? currentHandler = handler;
            while (currentHandler is DelegatingHandler delegatingHandler)
            {
                currentHandler = delegatingHandler.InnerHandler;

                if (currentHandler?.GetType().FullName == handlerTypeName)
                {
                    return currentHandler;
                }
            }

            return null;
        }

        public static bool HasHttpHandlerType(HttpMessageHandler handler, Type handlerType)
        {
            return GetHttpHandlerType(handler, handlerType) != null;
        }

        public static HttpMessageHandler? GetHttpHandlerType(HttpMessageHandler handler, Type handlerType)
        {
            if (handler?.GetType() == handlerType)
            {
                return handler;
            }

            HttpMessageHandler? currentHandler = handler;
            while (currentHandler is DelegatingHandler delegatingHandler)
            {
                currentHandler = delegatingHandler.InnerHandler;

                if (currentHandler?.GetType() == handlerType)
                {
                    return currentHandler;
                }
            }

            return null;
        }

        public static HttpHandlerType CalculateHandlerType(HttpMessageHandler handler)
        {
            if (HasHttpHandlerType(handler, "System.Net.Http.WinHttpHandler"))
            {
                return HttpHandlerType.WinHttpHandler;
            }
            if (HasHttpHandlerType(handler, "System.Net.Http.SocketsHttpHandler"))
            {
#if NET5_0_OR_GREATER
                var socketsHttpHandler = (SocketsHttpHandler)GetHttpHandlerType(handler, typeof(SocketsHttpHandler))!;
                if (socketsHttpHandler.ConnectCallback != null)
                {
                    return HttpHandlerType.Custom;
                }
#endif
                return HttpHandlerType.SocketsHttpHandler;
            }
            if (GetHttpHandlerType(handler, typeof(HttpClientHandler)) != null)
            {
                return HttpHandlerType.HttpClientHandler;
            }

            return HttpHandlerType.Custom;
        }
    }

    internal enum HttpHandlerType
    {
        SocketsHttpHandler,
        HttpClientHandler,
        WinHttpHandler,
        Custom
    }
}
