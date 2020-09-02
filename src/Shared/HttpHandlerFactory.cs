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

using System.Net.Http;

namespace Grpc.Shared
{
    internal static class HttpHandlerFactory
    {
        public static HttpMessageHandler CreatePrimaryHandler()
        {
#if NET5_0
            if (SocketsHttpHandler.IsSupported)
            {
                return new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true
                };
            }
            else
            {
                return new HttpClientHandler();
            }
#else
            return new HttpClientHandler();
#endif
        }

#if NET5_0
        public static HttpMessageHandler EnsureTelemetryHandler(HttpMessageHandler handler)
        {
            // HttpClientHandler has an internal handler that sets request telemetry header.
            // If the handler is SocketsHttpHandler then we know that the header will never be set
            // so wrap with a telemetry header setting handler.
            if (IsSocketsHttpHandler(handler))
            {
                return new TelemetryHeaderHandler(handler);
            }

            return handler;
        }

        private static bool IsSocketsHttpHandler(HttpMessageHandler handler)
        {
            if (handler is SocketsHttpHandler)
            {
                return true;
            }

            HttpMessageHandler? currentHandler = handler;
            DelegatingHandler? delegatingHandler;
            while ((delegatingHandler = currentHandler as DelegatingHandler) != null)
            {
                currentHandler = delegatingHandler.InnerHandler;

                if (currentHandler is SocketsHttpHandler)
                {
                    return true;
                }
            }

            return false;
        }
#endif
    }
}
