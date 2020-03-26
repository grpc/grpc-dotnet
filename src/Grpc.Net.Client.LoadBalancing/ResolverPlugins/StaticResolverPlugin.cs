using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Grpc.Net.Client.LoadBalancing.ResolverPlugins
{
    /// <summary>
    /// Resolver plugin is responsible for name resolution by reaching the authority and return 
    /// a list of resolved addresses (both IP address and port) and a service config.
    /// More: https://github.com/grpc/grpc/blob/master/doc/naming.md
    /// </summary>
    public sealed class StaticResolverPlugin : IGrpcResolverPlugin
    {
        private readonly Func<Uri, List<GrpcNameResolutionResult>> _staticNameResolution;
        private ILogger _logger = NullLogger.Instance;

        /// <summary>
        /// LoggerFactory is configured (injected) when class is being instantiated.
        /// </summary>
        public ILoggerFactory LoggerFactory
        {
            set => _logger = value.CreateLogger<StaticResolverPlugin>();
        }

        /// <summary>
        /// Creates a <seealso cref="StaticResolverPlugin"/> with configation passed as function parameter.  
        /// </summary>
        /// <param name="staticNameResolution"></param>
        public StaticResolverPlugin(Func<Uri, List<GrpcNameResolutionResult>> staticNameResolution)
        {
            if(staticNameResolution == null)
            {
                throw new ArgumentNullException(nameof(staticNameResolution));
            }
            _staticNameResolution = staticNameResolution;
        }

        /// <summary>
        /// Name resolution for secified target.
        /// </summary>
        /// <param name="target">Server address with scheme.</param>
        /// <returns>List of resolved servers and/or lookaside load balancers.</returns>
        public Task<List<GrpcNameResolutionResult>> StartNameResolutionAsync(Uri target)
        {
            _logger.LogDebug($"Using static name resolution");
            return Task.FromResult(_staticNameResolution(target));
        }
    }
}
