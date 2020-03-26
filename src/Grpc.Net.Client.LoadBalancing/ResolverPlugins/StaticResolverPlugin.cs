#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Grpc.Net.Client.LoadBalancing.ResolverPlugins
{
    public sealed class StaticResolverPlugin : IGrpcResolverPlugin
    {
        private readonly Func<Uri, List<GrpcNameResolutionResult>> _staticNameResolution;
        private ILogger _logger = NullLogger.Instance;

        public ILoggerFactory LoggerFactory
        {
            set => _logger = value.CreateLogger<StaticResolverPlugin>();
        }

        public StaticResolverPlugin(Func<Uri, List<GrpcNameResolutionResult>> staticNameResolution)
        {
            if(staticNameResolution == null)
            {
                throw new ArgumentNullException(nameof(staticNameResolution));
            }
            _staticNameResolution = staticNameResolution;
        }

        public Task<List<GrpcNameResolutionResult>> StartNameResolutionAsync(Uri target)
        {
            _logger.LogDebug($"Using static name resolution");
            return Task.FromResult(_staticNameResolution(target));
        }
    }
}
