using Grpc.Core;
using System;
using System.Threading;

namespace protobuf_net.Grpc
{
    public readonly struct CallContext
    {
        public CallOptions? Client => Server == null ? _client : default;
        public ServerCallContext Server { get; }

        public Metadata RequestHeaders => Server == null ? _client.Headers : Server.RequestHeaders;
        public CancellationToken CancellationToken => Server == null ? _client.CancellationToken : Server.CancellationToken;
        public DateTime? Deadline => Server == null ? _client.Deadline : Server.Deadline;

        private readonly CallOptions _client;

        public CallContext(ServerCallContext context)
        {
            _client = default;
            Server = context;
        }
        public CallContext(CallOptions client)
        {
            _client = client;
            Server = default;
        }
    }
}
