using System;
using System.Threading;
using Grpc.Core;
using Grpc.Lb.V1;

namespace Grpc.Net.Client.LoadBalancing.Policies.Abstraction
{
    /// <summary>
    /// This abstraction was added to the code base to make policies easy to mock in testing scenarios.
    /// </summary>
    internal interface ILoadBalancerClient : IDisposable
    {
        public IAsyncDuplexStreamingCall<LoadBalanceRequest, LoadBalanceResponse> BalanceLoad(Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default);
        public IAsyncDuplexStreamingCall<LoadBalanceRequest, LoadBalanceResponse> BalanceLoad(CallOptions options);
    }
}
