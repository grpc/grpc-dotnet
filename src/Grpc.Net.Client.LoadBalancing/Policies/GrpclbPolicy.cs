using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Grpc.Lb.V1;
using System.Threading;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using Grpc.Net.Client.LoadBalancing.Policies.Abstraction;

namespace Grpc.Net.Client.LoadBalancing.Policies
{
    /// <summary>
    /// The load balancing policy creates a subchannel to each server address.
    /// For each RPC sent, the load balancing policy decides which subchannel (i.e., which server) the RPC should be sent to.
    /// 
    /// Official name of this policy is "grpclb". It is a implementation of an external load balancing also called lookaside or one-arm loadbalancing.
    /// More: https://github.com/grpc/grpc/blob/master/doc/load-balancing.md#external-load-balancing-service
    /// </summary>
    public sealed class GrpclbPolicy : IGrpcLoadBalancingPolicy
    {
        private int _i = -1;
        private ILogger _logger = NullLogger.Instance;
        private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

        /// <summary>
        /// LoggerFactory is configured (injected) when class is being instantiated.
        /// </summary>
        public ILoggerFactory LoggerFactory
        {
            set
            {
                _loggerFactory = value;
                _logger = value.CreateLogger<GrpclbPolicy>();
            }
        }

        /// <summary>
        /// Property created for testing purposes, allows setter injection
        /// </summary>
        internal ILoadBalancerClient? OverrideLoadBalancerClient { private get; set; }

        /// <summary>
        /// Creates a subchannel to each server address. Depending on policy this may require additional 
        /// steps eg. reaching out to lookaside loadbalancer.
        /// </summary>
        /// <param name="resolutionResult">Resolved list of servers and/or lookaside load balancers.</param>
        /// <param name="isSecureConnection">Flag if connection between client and destination server should be secured.</param>
        /// <returns>List of subchannels.</returns>
        public async Task<List<GrpcSubChannel>> CreateSubChannelsAsync(List<GrpcNameResolutionResult> resolutionResult, bool isSecureConnection)
        {
            if (resolutionResult == null)
            {
                throw new ArgumentNullException(nameof(resolutionResult));
            }
            resolutionResult = resolutionResult.Where(x => x.IsLoadBalancer).ToList();
            if (resolutionResult.Count == 0)
            {
                throw new ArgumentException($"{nameof(resolutionResult)} must contain at least one blancer address");
            }
            _logger.LogDebug($"Start grpclb policy");
            _logger.LogDebug($"Start connection to external load balancer");
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            var channelOptionsForLB = new GrpcChannelOptions()
            {
                LoggerFactory = _loggerFactory
            };
            using var loadBalancerClient = GetLoadBalancerClient(resolutionResult, channelOptionsForLB);
            var balancingStreaming = loadBalancerClient.BalanceLoad();
            var initialRequest = new InitialLoadBalanceRequest() { Name = "service-name" }; //TODO remove hardcoded value
            await balancingStreaming.RequestStream.WriteAsync(new LoadBalanceRequest() { InitialRequest = initialRequest }).ConfigureAwait(false);
            var clientStats = new ClientStats();
            await balancingStreaming.RequestStream.WriteAsync(new LoadBalanceRequest() { ClientStats = clientStats }).ConfigureAwait(false);
            await balancingStreaming.RequestStream.CompleteAsync().ConfigureAwait(false);
            var result = new List<GrpcSubChannel>();
            while (await balancingStreaming.ResponseStream.MoveNext(CancellationToken.None).ConfigureAwait(false))
            {
                var loadBalanceResponse = balancingStreaming.ResponseStream.Current;
                if (loadBalanceResponse.LoadBalanceResponseTypeCase == LoadBalanceResponse.LoadBalanceResponseTypeOneofCase.ServerList)
                {
                    foreach (var server in loadBalanceResponse.ServerList.Servers)
                    {
                        var ipAddress = new IPAddress(server.IpAddress.ToByteArray()).ToString();
                        var uriBuilder = new UriBuilder();
                        uriBuilder.Host = ipAddress;
                        uriBuilder.Port = server.Port;
                        uriBuilder.Scheme = isSecureConnection ? "https" : "http";
                        var uri = uriBuilder.Uri;
                        result.Add(new GrpcSubChannel(uri));
                        _logger.LogDebug($"Found a server {uri}");
                    }
                }
            }
            _logger.LogDebug($"SubChannels list created");
            return result;
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

        private ILoadBalancerClient GetLoadBalancerClient(List<GrpcNameResolutionResult> resolutionResult, GrpcChannelOptions channelOptionsForLB)
        {
            if(OverrideLoadBalancerClient != null)
            {
                return OverrideLoadBalancerClient;
            }
            return new WrappedLoadBalancerClient(resolutionResult, channelOptionsForLB);
        }
    }
}
