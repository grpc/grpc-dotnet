using System;
using System.Collections.Generic;
using System.Threading;
using Grpc.Core;
using Grpc.Lb.V1;

namespace Grpc.Net.Client.LoadBalancing.Policies.Abstraction
{
    /// <summary>
    /// This class wrap and delegate LoadBalancerClient.
    /// The reason why it was added described here <seealso cref="ILoadBalancerClient"/>  
    /// </summary>
    internal sealed class WrappedLoadBalancerClient : ILoadBalancerClient
    {
        private readonly GrpcChannel _channelForLB;
        private readonly LoadBalancer.LoadBalancerClient _loadBalancerClient;
        public WrappedLoadBalancerClient(List<GrpcNameResolutionResult> resolutionResult, GrpcChannelOptions channelOptionsForLB)
        {
            _channelForLB = GrpcChannel.ForAddress($"http://{resolutionResult[0].Host}:{resolutionResult[0].Port}", channelOptionsForLB);
            _loadBalancerClient = new LoadBalancer.LoadBalancerClient(_channelForLB);
        }

        public IAsyncDuplexStreamingCall<LoadBalanceRequest, LoadBalanceResponse> BalanceLoad(CallOptions options)
        {
            return new WrappedAsyncDuplexStreamingCall<LoadBalanceRequest, LoadBalanceResponse>(_loadBalancerClient.BalanceLoad(options));
        }

        public IAsyncDuplexStreamingCall<LoadBalanceRequest, LoadBalanceResponse> BalanceLoad(Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            return new WrappedAsyncDuplexStreamingCall<LoadBalanceRequest, LoadBalanceResponse>(_loadBalancerClient.BalanceLoad(headers, deadline, cancellationToken));
        }

        public void Dispose()
        {
            _channelForLB.Dispose();
        }
    }
}
