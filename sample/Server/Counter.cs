using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Count;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace GRPCServer
{
    public class CounterService : Counter.CounterBase
    {
        private readonly ILogger _logger;
        private readonly IncrementingCounter _counter;

        public CounterService(IncrementingCounter counter, ILoggerFactory loggerFactory)
        {
            _counter = counter;
            _logger = loggerFactory.CreateLogger<CounterService>();
        }

        public override Task<CounterReply> IncrementCount(Empty request, ServerCallContext context)
        {
            _logger.LogInformation("Incrementing count by 1");
            _counter.Increment(1);
            return Task.FromResult(new CounterReply { Count = _counter.Count });
        }

        public override async Task<CounterReply> AccumulateCount(IAsyncStreamReader<CounterRequest> requestStream, ServerCallContext context)
        {
            while (await requestStream.MoveNext(CancellationToken.None))
            {
                _logger.LogInformation($"Incrementing count by {requestStream.Current.Count}");

                _counter.Increment(requestStream.Current.Count);
            }

            return new CounterReply { Count = _counter.Count };
        }
    }
}
