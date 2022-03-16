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

using Grpc.Shared;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using QpsWorker.Infrastructure;
using QpsWorker.Services;

var configRoot = ConfigHelpers.GetConfiguration();
if (!int.TryParse(configRoot["driver_port"], out var port))
{
    throw new InvalidOperationException("driver_port argument not specified.");
}

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Debug);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
});

var services = builder.Services;
services.AddGrpc(o => o.EnableDetailedErrors = true);
services.AddGrpcReflection();

var app = builder.Build();
app.UseMiddleware<ServiceProvidersMiddleware>();
app.MapGrpcService<WorkerServiceImpl>();
app.MapGrpcReflectionService();
app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() =>
{
    if (Enum.TryParse<LogLevel>(configRoot["LogLevel"], out var logLevel))
    {
        app.Logger.LogInformation($"Client and server logging enabled with level '{logLevel}'");
    }
});
app.Run();
