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
using Count;
using Greet;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Server
{
    public partial class Startup
    {
        private readonly IConfiguration _configuration;
        private const string EnableOpenTelemetryKey = "EnableOpenTelemetry";

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddGrpc();
            services.AddSingleton<IncrementingCounter>();

            if (bool.TryParse(_configuration[EnableOpenTelemetryKey], out var enableOpenTelemetry) && enableOpenTelemetry)
            {
                services.AddOpenTelemetryTracing(telemetry =>
                {
                    telemetry.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("aggregator"));
                    telemetry.AddZipkinExporter();
                    telemetry.AddGrpcClientInstrumentation();
                    telemetry.AddHttpClientInstrumentation();
                    telemetry.AddAspNetCoreInstrumentation();
                });
            }

            // These clients will call back to the server
            services
                .AddGrpcClient<Greeter.GreeterClient>((s, o) => { o.Address = GetCurrentAddress(s); })
                .EnableCallContextPropagation();
            services
                .AddGrpcClient<Counter.CounterClient>((s, o) => { o.Address = GetCurrentAddress(s); })
                .EnableCallContextPropagation();

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
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<GreeterService>();
                endpoints.MapGrpcService<CounterService>();
                endpoints.MapGrpcService<AggregatorService>();
            });
        }
    }
}
