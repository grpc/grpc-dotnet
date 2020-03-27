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
    /// The load balancing policy creates a subchannel to each server address.
    /// For each RPC sent, the load balancing policy decides which subchannel (i.e., which server) the RPC should be sent to.
    /// 
    /// Official name of this policy is "round_robin". It is a implementation of an balancing-aware client.
    /// More: https://github.com/grpc/grpc/blob/master/doc/load-balancing.md#balancing-aware-client
    /// </summary>
    public sealed class RoundRobinPolicy : IGrpcLoadBalancingPolicy
    {
        private int _i = -1;
        private ILogger _logger = NullLogger.Instance;

        /// <summary>
        /// LoggerFactory is configured (injected) when class is being instantiated.
        /// </summary>
        public ILoggerFactory LoggerFactory
        {
            set => _logger = value.CreateLogger<RoundRobinPolicy>();
        }

        /// <summary>
        /// Creates a subchannel to each server address. Depending on policy this may require additional 
        /// steps eg. reaching out to lookaside loadbalancer.
        /// </summary>
        /// <param name="resolutionResult">Resolved list of servers and/or lookaside load balancers.</param>
        /// <param name="isSecureConnection">Flag if connection between client and destination server should be secured.</param>
        /// <returns>List of subchannels.</returns>
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
                var uriBuilder = new UriBuilder();
                uriBuilder.Host = x.Host;
                uriBuilder.Port = x.Port ?? (isSecureConnection ? 443 : 80);
                uriBuilder.Scheme = isSecureConnection ? "https" : "http";
                var uri = uriBuilder.Uri;
                _logger.LogDebug($"Found a server {uri}");
                return new GrpcSubChannel(uri);
            }).ToList();
            _logger.LogDebug($"SubChannels list created");
            return Task.FromResult(result);
        }

        /// <summary>
        /// For each RPC sent, the load balancing policy decides which subchannel (i.e., which server) the RPC should be sent to.
        /// </summary>
        /// <param name="subChannels">List of subchannels.</param>
        /// <returns>Selected subchannel.</returns>
        public GrpcSubChannel GetNextSubChannel(List<GrpcSubChannel> subChannels)
        {
            return subChannels[Interlocked.Increment(ref _i) % subChannels.Count];
        }
    }
}
