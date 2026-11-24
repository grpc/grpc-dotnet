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

#if SUPPORT_LOAD_BALANCING
using System.Net;

namespace Grpc.Net.Client.Balancer.Internal;

/// <summary>
/// Predicts whether SocketsHttpHandler sends a request through a proxy.
/// The rules mirror HttpConnectionPoolManager in System.Net.Http.
/// </summary>
internal static class ProxyHelpers
{
    /// <summary>
    /// Gets the proxy that SocketsHttpHandler uses, or null when the handler doesn't use a proxy.
    /// </summary>
    public static IWebProxy? GetProxy(SocketsHttpHandler socketsHttpHandler)
    {
        if (!socketsHttpHandler.UseProxy)
        {
            return null;
        }

        return socketsHttpHandler.Proxy ?? HttpClient.DefaultProxy;
    }

    /// <summary>
    /// Returns true when a request to the endpoint is sent through the proxy.
    /// </summary>
    public static bool IsProxied(IWebProxy? proxy, DnsEndPoint endPoint, bool isSecure)
    {
        if (proxy == null)
        {
            return false;
        }

        // BalancerHttpHandler sends requests to a URI with the endpoint's host and port.
        var requestUri = new UriBuilder(isSecure ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, endPoint.Host, endPoint.Port).Uri;

        try
        {
            return !proxy.IsBypassed(requestUri) && proxy.GetProxy(requestUri) != null;
        }
        catch
        {
            // SocketsHttpHandler treats an error from the proxy as no proxy.
            return false;
        }
    }
}
#endif
