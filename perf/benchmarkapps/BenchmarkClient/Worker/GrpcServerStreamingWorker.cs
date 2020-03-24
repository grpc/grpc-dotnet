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

using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkClient.ChannelFactory;
using Grpc.Core;
using Grpc.Testing;

namespace BenchmarkClient.Worker
{
    public class GrpcServerStreamingWorker : IWorker
    {
        private readonly int _connectionId;
        private readonly IChannelFactory _channelFactory;
        private readonly DateTime? _deadline;
        private readonly CancellationTokenSource _cts;
        private ChannelBase? _channel;
        private BenchmarkService.BenchmarkServiceClient? _client;
        private AsyncServerStreamingCall<SimpleResponse>? _call;

        public GrpcServerStreamingWorker(int connectionId, int streamId, IChannelFactory channelFactory, DateTime? deadline = null)
        {
            Id = connectionId + "-" + streamId;
            _connectionId = connectionId;
            _channelFactory = channelFactory;
            _deadline = deadline;
            _cts = new CancellationTokenSource();
        }

        public string Id { get; }

        public async Task CallAsync()
        {
            try
            {
                if (!await _call!.ResponseStream.MoveNext())
                {
                    throw new Exception("Unexpected end of stream.");
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && _cts.IsCancellationRequested)
            {
                // Expected exception from canceling call
            }
        }

        public async Task ConnectAsync()
        {
            _channel = await _channelFactory.CreateAsync(_connectionId);
            _client = new BenchmarkService.BenchmarkServiceClient(_channel);

            var options = new CallOptions(deadline: _deadline, cancellationToken: _cts.Token);
            _call = _client.StreamingFromServer(new SimpleRequest { ResponseSize = 10 }, options);
        }

        public async Task DisconnectAsync()
        {
            _cts.Cancel();
            _call?.Dispose();
            if (_channel != null)
            {
                await _channelFactory.DisposeAsync(_channel);
            }
        }
    }
}
