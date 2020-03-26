using System;
using System.Net;

namespace Grpc.Net.Client.LoadBalancing.ResolverPlugins
{
    /// <summary>
    /// An options class for configuring a <see cref="DnsClientResolverPlugin"/>.
    /// </summary>
    public sealed class DnsClientResolverPluginOptions
    {
        /// <summary>
        /// Allows override dns nameservers used during lookup. Default value is an empty list.
        /// If an empty list is specified client defaults to machine list of nameservers.
        /// </summary>
        public IPEndPoint[] NameServers { get; set; }
        
        /// <summary>
        /// Allows disabling service config lookup. Default value false.
        /// </summary>
        public bool DisableTxtServiceConfig { get; set; }

        /// <summary>
        /// Creates a <seealso cref="DnsClientResolverPluginOptions"/> options class with default values.
        /// </summary>
        public DnsClientResolverPluginOptions()
        {
            NameServers = Array.Empty<IPEndPoint>();
            DisableTxtServiceConfig = false;
        }
    }
}
