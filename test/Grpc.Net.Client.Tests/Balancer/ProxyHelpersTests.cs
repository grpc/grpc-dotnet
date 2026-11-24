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
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Grpc.Net.Client.Balancer.Internal;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Balancer;

// Some tests change HttpClient.DefaultProxy, which is global to the process.
[TestFixture]
[NonParallelizable]
public class ProxyHelpersTests
{
    private static readonly Uri ProxyUri = new Uri("http://proxy.example:8080");

    private sealed class TestWebProxy : IWebProxy
    {
        public List<Uri> GetProxyUris { get; } = new List<Uri>();
        public bool Bypassed { get; set; }
        public Uri? Proxy { get; set; } = ProxyUri;
        public Exception? Error { get; set; }
        public ICredentials? Credentials { get; set; }

        public Uri? GetProxy(Uri destination)
        {
            if (Error != null)
            {
                throw Error;
            }
            GetProxyUris.Add(destination);
            return Proxy;
        }

        public bool IsBypassed(Uri host)
        {
            if (Error != null)
            {
                throw Error;
            }
            return Bypassed;
        }
    }

    [Test]
    public void GetProxy_UseProxyFalse_ReturnsNull()
    {
        var handler = new SocketsHttpHandler { UseProxy = false, Proxy = new TestWebProxy() };

        Assert.IsNull(ProxyHelpers.GetProxy(handler));
    }

    [Test]
    public void GetProxy_ProxySet_ReturnsProxy()
    {
        var proxy = new TestWebProxy();
        var handler = new SocketsHttpHandler { Proxy = proxy };

        Assert.AreSame(proxy, ProxyHelpers.GetProxy(handler));
    }

    [Test]
    public void GetProxy_ProxyNotSet_ReturnsDefaultProxy()
    {
        var previous = HttpClient.DefaultProxy;
        var defaultProxy = new TestWebProxy();
        HttpClient.DefaultProxy = defaultProxy;
        try
        {
            Assert.AreSame(defaultProxy, ProxyHelpers.GetProxy(new SocketsHttpHandler()));
        }
        finally
        {
            HttpClient.DefaultProxy = previous;
        }
    }

    [Test]
    public void IsProxied_NoProxy_ReturnsFalse()
    {
        Assert.IsFalse(ProxyHelpers.IsProxied(null, new DnsEndPoint("localhost", 80), isSecure: false));
    }

    [TestCase(false, "http://localhost:5000/")]
    [TestCase(true, "https://localhost:5000/")]
    public void IsProxied_ProxyApplies_ReturnsTrue(bool isSecure, string expectedUri)
    {
        var proxy = new TestWebProxy();

        Assert.IsTrue(ProxyHelpers.IsProxied(proxy, new DnsEndPoint("localhost", 5000), isSecure));
        Assert.AreEqual(new Uri(expectedUri), proxy.GetProxyUris[0]);
    }

    [Test]
    public void IsProxied_IPv6Address_UsesBracketedHost()
    {
        var proxy = new TestWebProxy();

        Assert.IsTrue(ProxyHelpers.IsProxied(proxy, new DnsEndPoint("::1", 5000), isSecure: true));
        Assert.AreEqual(new Uri("https://[::1]:5000/"), proxy.GetProxyUris[0]);
    }

    [Test]
    public void IsProxied_Bypassed_ReturnsFalse()
    {
        // WebProxy.GetProxy returns the destination for a bypassed address, so IsBypassed must be checked.
        var proxy = new TestWebProxy { Bypassed = true };

        Assert.IsFalse(ProxyHelpers.IsProxied(proxy, new DnsEndPoint("localhost", 5000), isSecure: true));
    }

    [Test]
    public void IsProxied_WebProxyBypassList_ReturnsFalse()
    {
        var proxy = new WebProxy(ProxyUri) { BypassList = new[] { @"internal\.example" } };

        Assert.IsFalse(ProxyHelpers.IsProxied(proxy, new DnsEndPoint("internal.example", 443), isSecure: true));
        Assert.IsTrue(ProxyHelpers.IsProxied(proxy, new DnsEndPoint("external.example", 443), isSecure: true));
    }

    [Test]
    public void IsProxied_GetProxyReturnsNull_ReturnsFalse()
    {
        var proxy = new TestWebProxy { Proxy = null };

        Assert.IsFalse(ProxyHelpers.IsProxied(proxy, new DnsEndPoint("localhost", 5000), isSecure: true));
    }

    [Test]
    public void IsProxied_ProxyThrows_ReturnsFalse()
    {
        var proxy = new TestWebProxy { Error = new InvalidOperationException("Error!") };

        Assert.IsFalse(ProxyHelpers.IsProxied(proxy, new DnsEndPoint("localhost", 5000), isSecure: true));
    }
}
#endif
