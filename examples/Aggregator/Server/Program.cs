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

using Count;
using Greet;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
builder.Services.AddSingleton<IncrementingCounter>();

if (bool.TryParse(builder.Configuration["EnableOpenTelemetry"], out var enableOpenTelemetry) && enableOpenTelemetry)
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("aggregator"));

            if (builder.Environment.IsDevelopment())
            {
                // We want to view all traces in development
                tracing.SetSampler(new AlwaysOnSampler());
            }

            tracing.AddAspNetCoreInstrumentation()
                   .AddGrpcClientInstrumentation()
                   .AddHttpClientInstrumentation();

            tracing.AddZipkinExporter();
        });
}

// These clients will call back to the server
builder.Services
    .AddGrpcClient<Greeter.GreeterClient>((s, o) => { o.Address = GetCurrentAddress(s); })
    .EnableCallContextPropagation();
builder.Services
    .AddGrpcClient<Counter.CounterClient>((s, o) => { o.Address = GetCurrentAddress(s); })
    .EnableCallContextPropagation();

var app = builder.Build();
app.MapGrpcService<GreeterService>();
app.MapGrpcService<CounterService>();
app.MapGrpcService<AggregatorService>();
app.Run();

static Uri GetCurrentAddress(IServiceProvider serviceProvider)
{
    // Get the address of the current server from the request
    var context = serviceProvider.GetRequiredService<IHttpContextAccessor>()?.HttpContext;
    if (context == null)
    {
        throw new InvalidOperationException("Could not get HttpContext.");
    }

    return new Uri($"{context.Request.Scheme}://{context.Request.Host.Value}");
}
