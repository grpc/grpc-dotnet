#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.Net;

namespace Grpc.Net.Client.LoadBalancing.ResolverPlugins
{
    public sealed class DnsClientResolverPluginOptions
    {
        public IPEndPoint[] NameServers { get; set; }
        public bool DisableTxtServiceConfig { get; set; }

        public DnsClientResolverPluginOptions()
        {
            NameServers = Array.Empty<IPEndPoint>();
            DisableTxtServiceConfig = false;
        }
    }
}
