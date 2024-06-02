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

using System.Diagnostics.CodeAnalysis;
using Grpc.Net.Client;

namespace Grpc.Shared;

internal static class HttpHandlerFactory
{
    public static bool TryCreatePrimaryHandler([NotNullWhen(true)] out HttpMessageHandler? primaryHandler)
    {
#if NET5_0_OR_GREATER
        // If we're in .NET 5 and SocketsHttpHandler is supported (it's not in Blazor WebAssembly)
        // then create SocketsHttpHandler with EnableMultipleHttp2Connections set to true. That will
        // allow a gRPC channel to create new connections if the maximum allow concurrency is exceeded.
        if (SocketsHttpHandler.IsSupported)
        {
            primaryHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true
            };
            return true;
        }
#endif

#if NET462
        // Create WinHttpHandler with EnableMultipleHttp2Connections set to true. That will
        // allow a gRPC channel to create new connections if the maximum allow concurrency is exceeded.
        primaryHandler = new WinHttpHandler
        {
            EnableMultipleHttp2Connections = true
        };
        return true;
#elif !NETSTANDARD2_0
        primaryHandler = new HttpClientHandler();
        return true;
#else
        primaryHandler = null;
        return false;
#endif
    }

    public static Exception CreateUnsupportedHandlerException()
    {
        var message =
            $"gRPC requires extra configuration on .NET implementations that don't support gRPC over HTTP/2. " +
            $"An HTTP provider must be specified using {nameof(GrpcChannelOptions)}.{nameof(GrpcChannelOptions.HttpHandler)}." +
            $"The configured HTTP provider must either support HTTP/2 or be configured to use gRPC-Web. " +
            $"See https://aka.ms/aspnet/grpc/netstandard for details.";
        return new PlatformNotSupportedException(message);
    }
}
