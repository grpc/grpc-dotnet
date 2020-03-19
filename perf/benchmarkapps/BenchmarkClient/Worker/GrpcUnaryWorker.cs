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

using System.Threading.Tasks;
using BenchmarkClient.ChannelFactory;
using Grpc.Core;
using Grpc.Testing;

namespace BenchmarkClient.Worker
{
    public class GrpcUnaryWorker : IWorker
    {
        private ChannelBase? _channel;
        private BenchmarkService.BenchmarkServiceClient? _client;
        private readonly int _connectionId;
        private readonly IChannelFactory _channelFactory;

        public GrpcUnaryWorker(int connectionId, int streamId, IChannelFactory channelFactory)
        {
            Id = connectionId + "-" + streamId;
            _connectionId = connectionId;
            _channelFactory = channelFactory;
        }

        public string Id { get; }

        public async Task CallAsync()
        {
            var call = _client!.UnaryCallAsync(new SimpleRequest { ResponseSize = 10 });
            await call.ResponseAsync;
        }

        public async Task ConnectAsync()
        {
            _channel = await _channelFactory.CreateAsync(_connectionId);
            _client = new BenchmarkService.BenchmarkServiceClient(_channel);
        }

        public async Task DisconnectAsync()
        {
            if (_channel != null)
            {
                await _channelFactory.DisposeAsync(_channel);
            }
        }
    }
}
