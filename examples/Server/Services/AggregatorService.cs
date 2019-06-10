#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System.Threading;
using System.Threading.Tasks;
using Aggregate;
using Count;
using Greet;
using Grpc.Core;

namespace GRPCServer
{
    public class AggregatorService : Aggregator.AggregatorBase
    {
        private readonly Greeter.GreeterClient _greeterClient;
        private readonly Counter.CounterClient _counterClient;

        public AggregatorService(
            Greeter.GreeterClient greeterClient,
            Counter.CounterClient counterClient)
        {
            _greeterClient = greeterClient;
            _counterClient = counterClient;
        }

        public override async Task<CounterReply> AccumulateCount(IAsyncStreamReader<CounterRequest> requestStream, ServerCallContext context)
        {
            // Forward the call on to the counter service
            using (var call = _counterClient.AccumulateCount())
            {
                while (await requestStream.MoveNext(CancellationToken.None))
                {
                    await call.RequestStream.WriteAsync(requestStream.Current);
                }

                await call.RequestStream.CompleteAsync();
                return await call;
            }
        }

        public override async Task SayHellos(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            // Forward the call on to the greeter service
            using (var call = _greeterClient.SayHellos(request))
            {
                while (await call.ResponseStream.MoveNext(CancellationToken.None))
                {
                    await responseStream.WriteAsync(call.ResponseStream.Current);
                }
            }
        }
    }
}
