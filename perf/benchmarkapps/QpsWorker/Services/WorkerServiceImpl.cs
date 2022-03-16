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

using Grpc.Core;
using Grpc.Testing;
using QpsWorker.Infrastructure;
using Void = Grpc.Testing.Void;

namespace QpsWorker.Services
{
    public class WorkerServiceImpl : WorkerService.WorkerServiceBase
    {
        private readonly ILogger<WorkerServiceImpl> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IHostApplicationLifetime _applicationLifetime;

        public WorkerServiceImpl(ILogger<WorkerServiceImpl> logger, ILoggerFactory loggerFactory, IHostApplicationLifetime applicationLifetime)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _applicationLifetime = applicationLifetime;
        }

        public override async Task RunServer(IAsyncStreamReader<ServerArgs> requestStream, IServerStreamWriter<ServerStatus> responseStream, ServerCallContext context)
        {
            if (!await requestStream.MoveNext())
            {
                throw new InvalidOperationException();
            }
            var serverConfig = requestStream.Current.Setup;
            var runner = ServerRunner.Start(_loggerFactory, serverConfig);
            try
            {
                await responseStream.WriteAsync(new ServerStatus
                {
                    Stats = runner.GetStats(false),
                    Port = runner.BoundPort,
                    Cores = Environment.ProcessorCount,
                });

                while (await requestStream.MoveNext())
                {
                    var reset = requestStream.Current.Mark.Reset;
                    await responseStream.WriteAsync(new ServerStatus
                    {
                        Stats = runner.GetStats(reset)
                    });
                }
            }
            finally
            {
                _logger.LogInformation("Exiting RunServer.");
                await runner.StopAsync();
            }
        }

        public override async Task RunClient(IAsyncStreamReader<ClientArgs> requestStream, IServerStreamWriter<ClientStatus> responseStream, ServerCallContext context)
        {
            if (!await requestStream.MoveNext())
            {
                throw new InvalidOperationException();
            }
            var clientConfig = requestStream.Current.Setup;
            var clientRunner = ClientRunner.Start(_loggerFactory, clientConfig);
            try
            {
                await responseStream.WriteAsync(new ClientStatus
                {
                    Stats = clientRunner.GetStats(false)
                });

                while (await requestStream.MoveNext())
                {
                    var reset = requestStream.Current.Mark.Reset;
                    await responseStream.WriteAsync(new ClientStatus
                    {
                        Stats = clientRunner.GetStats(reset)
                    });
                }
            }
            finally
            {
                _logger.LogInformation("Exiting RunClient.");
                await clientRunner.StopAsync();
            }
        }

        public override Task<CoreResponse> CoreCount(CoreRequest request, ServerCallContext context)
        {
            return Task.FromResult(new CoreResponse { Cores = Environment.ProcessorCount });
        }

        public override Task<Void> QuitWorker(Void request, ServerCallContext context)
        {
            _applicationLifetime.StopApplication();
            return Task.FromResult(new Void());
        }
    }
}
