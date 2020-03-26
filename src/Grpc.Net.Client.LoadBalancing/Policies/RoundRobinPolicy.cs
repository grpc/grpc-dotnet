#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.Net.Client.LoadBalancing.Policies
{
    /// <summary>
    /// round_robin policy
    /// </summary>
    public sealed class RoundRobinPolicy : IGrpcLoadBalancingPolicy
    {
        private ILogger _logger = NullLogger.Instance;
        public ILoggerFactory LoggerFactory
        {
            set => _logger = value.CreateLogger<RoundRobinPolicy>();
        }
        private int _i = 0;

        public Task<List<GrpcSubChannel>> CreateSubChannelsAsync(List<GrpcNameResolutionResult> resolutionResult, bool isSecureConnection)
        {
            if (resolutionResult == null)
            {
                throw new ArgumentNullException(nameof(resolutionResult));
            }
            resolutionResult = resolutionResult.Where(x => !x.IsLoadBalancer).ToList();
            if (resolutionResult.Count == 0)
            {
                throw new ArgumentException($"{nameof(resolutionResult)} must contain at least one non-blancer address");
            }
            _logger.LogDebug($"Start round_robin policy");
            var result = resolutionResult.Select(x =>
            {
                var uriBuilder = new UriBuilder($"{x.Host}:{x.Port}");
                uriBuilder.Scheme = isSecureConnection ? "https" : "http";
                var uri = uriBuilder.Uri;
                _logger.LogDebug($"Found a server {uri}");
                return new GrpcSubChannel(uri);
            }).ToList();
            _logger.LogDebug($"SubChannels list created");
            return Task.FromResult(result);
        }

        public GrpcSubChannel GetNextSubChannel(List<GrpcSubChannel> subChannels)
        {
            return subChannels[Interlocked.Increment(ref _i) % subChannels.Count];
        }
    }
}
