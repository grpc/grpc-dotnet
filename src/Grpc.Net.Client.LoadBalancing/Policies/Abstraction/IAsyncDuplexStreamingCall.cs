using System;
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.Net.Client.LoadBalancing.Policies.Abstraction
{
    /// <summary>
    /// This abstraction was added to the code base to make policies easy to mock in testing scenarios.
    /// </summary>
    internal interface IAsyncDuplexStreamingCall<TRequest, TResponse> : IDisposable
    {
        public IAsyncStreamReader<TResponse> ResponseStream { get; }
        public IClientStreamWriter<TRequest> RequestStream { get; }
        public Task<Metadata> ResponseHeadersAsync { get; }
        public Status GetStatus();
        public Metadata GetTrailers();
    }
}
