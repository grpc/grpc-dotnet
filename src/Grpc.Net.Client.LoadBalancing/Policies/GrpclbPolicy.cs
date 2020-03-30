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
using Google.Protobuf.WellKnownTypes;

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
        private TimeSpan _clientStatsReportInterval = TimeSpan.FromSeconds(10);
        private bool _isSecureConnection = false;
        private int _requestsCounter = 0;
        private int _subChannelsSelectionCounter = -1;
        private ILogger _logger = NullLogger.Instance;
        private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
        private ILoadBalancerClient? _loadBalancerClient;
        private IAsyncDuplexStreamingCall<LoadBalanceRequest, LoadBalanceResponse>? _balancingStreaming;
        private Timer? _timer;

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

        internal bool Disposed { get; private set; }

        internal IReadOnlyList<GrpcSubChannel> SubChannels { get; set; } = Array.Empty<GrpcSubChannel>();
        
        /// <summary>
        /// Property created for testing purposes, allows setter injection
        /// </summary>
        internal ILoadBalancerClient? OverrideLoadBalancerClient { private get; set; }

        /// <summary>
        /// Creates a subchannel to each server address. Depending on policy this may require additional 
        /// steps eg. reaching out to lookaside loadbalancer.
        /// </summary>
        /// <param name="resolutionResult">Resolved list of servers and/or lookaside load balancers.</param>
        /// <param name="serviceName">The name of the load balanced service (e.g., service.googleapis.com).</param>
        /// <param name="isSecureConnection">Flag if connection between client and destination server should be secured.</param>
        /// <returns>List of subchannels.</returns>
        public async Task CreateSubChannelsAsync(List<GrpcNameResolutionResult> resolutionResult, string serviceName, bool isSecureConnection)
        {
            if (resolutionResult == null)
            {
                throw new ArgumentNullException(nameof(resolutionResult));
            }
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                throw new ArgumentException($"{nameof(serviceName)} not defined");
            }
            resolutionResult = resolutionResult.Where(x => x.IsLoadBalancer).ToList();
            if (resolutionResult.Count == 0)
            {
                throw new ArgumentException($"{nameof(resolutionResult)} must contain at least one blancer address");
            }
            _isSecureConnection = isSecureConnection;
            _logger.LogDebug($"Start grpclb policy");
            _logger.LogDebug($"Start connection to external load balancer");
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            var channelOptionsForLB = new GrpcChannelOptions()
            {
                LoggerFactory = _loggerFactory
            };
            _loadBalancerClient = GetLoadBalancerClient(resolutionResult, channelOptionsForLB);
            _balancingStreaming = _loadBalancerClient.BalanceLoad();
            var initialRequest = new InitialLoadBalanceRequest() { Name = $"{serviceName}:{resolutionResult[0].Port}" };
            await _balancingStreaming.RequestStream.WriteAsync(new LoadBalanceRequest() { InitialRequest = initialRequest }).ConfigureAwait(false);
            await _balancingStreaming.ResponseStream.MoveNext(CancellationToken.None).ConfigureAwait(false);
            if (_balancingStreaming.ResponseStream.Current.LoadBalanceResponseTypeCase != LoadBalanceResponse.LoadBalanceResponseTypeOneofCase.InitialResponse)
            {
                throw new InvalidOperationException("InitialLoadBalanceRequest was not followed by InitialLoadBalanceResponse");
            }
            var initialResponse = _balancingStreaming.ResponseStream.Current.InitialResponse;
            _clientStatsReportInterval = initialResponse.ClientStatsReportInterval.ToTimeSpan();
            await _balancingStreaming.ResponseStream.MoveNext(CancellationToken.None).ConfigureAwait(false);
            if (_balancingStreaming.ResponseStream.Current.LoadBalanceResponseTypeCase != LoadBalanceResponse.LoadBalanceResponseTypeOneofCase.ServerList)
            {
                throw new InvalidOperationException("InitialLoadBalanceResponse was not followed by ServerList");
            }
            UpdateSubChannels(_balancingStreaming.ResponseStream.Current.ServerList);
            _logger.LogDebug($"SubChannels list created");
            _timer = new Timer(ReportClientStatsTimerAsync, null, _clientStatsReportInterval, _clientStatsReportInterval);
            _logger.LogDebug($"Periodic ClientStats reporting enabled");
        }

        /// <summary>
        /// For each RPC sent, the load balancing policy decides which subchannel (i.e., which server) the RPC should be sent to.
        /// </summary>
        /// <returns>Selected subchannel.</returns>
        public GrpcSubChannel GetNextSubChannel()
        {
            Interlocked.Increment(ref _requestsCounter);
            return SubChannels[Interlocked.Increment(ref _subChannelsSelectionCounter) % SubChannels.Count];
        }

        // async void recommended by Stephen Cleary https://stackoverflow.com/questions/38917818/pass-async-callback-to-timer-constructor
        private async void ReportClientStatsTimerAsync(object state)
        {
            await ReportClientStatsAsync().ConfigureAwait(false);
        }

        private async Task ReportClientStatsAsync()
        {
            var requestsCounter = Interlocked.Exchange(ref _requestsCounter, 0);
            var clientStats = new ClientStats()
            {
                NumCallsStarted = requestsCounter,
                NumCallsFinished = requestsCounter,
                NumCallsFinishedKnownReceived = requestsCounter,
                NumCallsFinishedWithClientFailedToSend = 0,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            await _balancingStreaming!.RequestStream.WriteAsync(new LoadBalanceRequest() { ClientStats = clientStats }).ConfigureAwait(false);
            await _balancingStreaming.ResponseStream.MoveNext(CancellationToken.None).ConfigureAwait(false);
            if (_balancingStreaming.ResponseStream.Current.LoadBalanceResponseTypeCase == LoadBalanceResponse.LoadBalanceResponseTypeOneofCase.ServerList)
            {
                UpdateSubChannels(_balancingStreaming.ResponseStream.Current.ServerList);
            }
        }

        private void UpdateSubChannels(ServerList serverList)
        {
            var result = new List<GrpcSubChannel>();
            foreach (var server in serverList.Servers)
            {
                var ipAddress = new IPAddress(server.IpAddress.ToByteArray()).ToString();
                var uriBuilder = new UriBuilder();
                uriBuilder.Host = ipAddress;
                uriBuilder.Port = server.Port;
                uriBuilder.Scheme = _isSecureConnection ? "https" : "http";
                var uri = uriBuilder.Uri;
                result.Add(new GrpcSubChannel(uri));
                _logger.LogDebug($"Found a server {uri}");
            }
            SubChannels = result;
        }

        private ILoadBalancerClient GetLoadBalancerClient(List<GrpcNameResolutionResult> resolutionResult, GrpcChannelOptions channelOptionsForLB)
        {
            if(OverrideLoadBalancerClient != null)
            {
                return OverrideLoadBalancerClient;
            }
            return new WrappedLoadBalancerClient(resolutionResult, channelOptionsForLB);
        }

        /// <summary>
        /// Releases the resources used by the <see cref="GrpclbPolicy"/> class.
        /// </summary>
        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }
            try
            {
                _timer?.Dispose();
                _balancingStreaming?.RequestStream.CompleteAsync().Wait(); // close request stream to complete gracefully
            }
            finally
            {
                _loadBalancerClient?.Dispose();
            }
            Disposed = true;
        }
    }
}
