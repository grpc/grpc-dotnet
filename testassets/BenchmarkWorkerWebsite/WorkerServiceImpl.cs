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
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkWorkerWebsite;
using Grpc.Core;
using Grpc.Core.Utils;
using Microsoft.Extensions.Logging;

namespace Grpc.Testing
{
    // modified version of https://github.com/grpc/grpc/blob/master/src/csharp/Grpc.IntegrationTesting/WorkerServiceImpl.cs
    public class WorkerServiceImpl : WorkerService.WorkerServiceBase
    {
        readonly ILogger logger;

        public WorkerServiceImpl(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger<WorkerServiceImpl>();
        }
        
        public override async Task RunServer(IAsyncStreamReader<ServerArgs> requestStream, IServerStreamWriter<ServerStatus> responseStream, ServerCallContext context)
        {
            GrpcPreconditions.CheckState(await requestStream.MoveNext());
            var serverConfig = requestStream.Current.Setup;
            var runner = ServerRunners.CreateStarted(serverConfig, logger);

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
            await runner.StopAsync();
        }

        public override Task RunClient(IAsyncStreamReader<ClientArgs> requestStream, IServerStreamWriter<ClientStatus> responseStream, ServerCallContext context)
        {
            throw new NotImplementedException("Clients are not yet supported.");
        }

        public override Task<CoreResponse> CoreCount(CoreRequest request, ServerCallContext context)
        {
            return Task.FromResult(new CoreResponse { Cores = Environment.ProcessorCount });
        }

        public override Task<Void> QuitWorker(Void request, ServerCallContext context)
        {
            Program.QuitWorker();
            return Task.FromResult(new Void());
        }
    }
}
