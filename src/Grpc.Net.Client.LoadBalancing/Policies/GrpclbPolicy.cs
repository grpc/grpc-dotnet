#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Grpc.Lb.V1;
using System.Threading;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;

namespace Grpc.Net.Client.LoadBalancing.Policies
{
    /// <summary>
    /// grpclb policy
    /// </summary>
    public sealed class GrpclbPolicy : IGrpcLoadBalancingPolicy
    {
        private ILogger _logger = NullLogger.Instance;
        private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
        public ILoggerFactory LoggerFactory
        {
            set
            {
                _loggerFactory = value;
                _logger = value.CreateLogger<GrpclbPolicy>();
            }
        }

        private int _i = 0;

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
            using var channelForLB = GrpcChannel.ForAddress($"http://{resolutionResult[0].Host}:{resolutionResult[0].Port}", channelOptionsForLB);
            var loadBalancerClient = new LoadBalancer.LoadBalancerClient(channelForLB);
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
                        var uriBuilder = new UriBuilder($"{ipAddress}:{server.Port}");
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

        public GrpcSubChannel GetNextSubChannel(List<GrpcSubChannel> subChannels)
        {
            return subChannels[Interlocked.Increment(ref _i) % subChannels.Count];
        }
    }
}
