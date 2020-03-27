using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.Net.Client.LoadBalancing.Policies.Abstraction
{
    /// <summary>
    /// This class wrap and delegate AsyncDuplexStreamingCall.
    /// The reason why it was added described here <seealso cref="IAsyncDuplexStreamingCall{TRequest, TResponse}"/>  
    /// </summary>
    internal sealed class WrappedAsyncDuplexStreamingCall<TRequest, TResponse> : IAsyncDuplexStreamingCall<TRequest, TResponse>
    {
        private readonly AsyncDuplexStreamingCall<TRequest, TResponse> _asyncDuplexStreamingCall;

        public WrappedAsyncDuplexStreamingCall(AsyncDuplexStreamingCall<TRequest, TResponse> asyncDuplexStreamingCall)
        {
            _asyncDuplexStreamingCall = asyncDuplexStreamingCall;
        }
        public IAsyncStreamReader<TResponse> ResponseStream => _asyncDuplexStreamingCall.ResponseStream;

        public IClientStreamWriter<TRequest> RequestStream => _asyncDuplexStreamingCall.RequestStream;

        public Task<Metadata> ResponseHeadersAsync => _asyncDuplexStreamingCall.ResponseHeadersAsync;

        public void Dispose() => _asyncDuplexStreamingCall.Dispose();

        public Status GetStatus() => _asyncDuplexStreamingCall.GetStatus();

        public Metadata GetTrailers() => _asyncDuplexStreamingCall.GetTrailers();
    }
}
