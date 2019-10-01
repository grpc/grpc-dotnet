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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Collector.AspNetCore;
using OpenTelemetry.Collector.Dependencies;
using OpenTelemetry.Exporter.Zipkin;
using OpenTelemetry.Trace;
using OpenTelemetry.Trace.Config;
using OpenTelemetry.Trace.Export;
using OpenTelemetry.Trace.Sampler;

namespace Server
{
    public partial class Startup
    {
        private void ConfigureOpenTelemetryServices(IServiceCollection services)
        {
            services.AddSingleton<ISampler>(Samplers.AlwaysSample);
            services.AddSingleton<ZipkinTraceExporterOptions>(_ => new ZipkinTraceExporterOptions
            {
                ServiceName = "aggregator",
                Endpoint = new Uri("http://localhost:9411/api/v2/spans")
            });
            services.AddSingleton<SpanExporter, ZipkinTraceExporter>();
            services.AddSingleton<SpanProcessor, BatchingSpanProcessor>();
            services.AddSingleton<TraceConfig>();
            services.AddSingleton<ITracer, Tracer>();

            // you may also configure request and dependencies collectors
            services.AddSingleton<RequestsCollectorOptions>(new RequestsCollectorOptions());
            services.AddSingleton<RequestsCollector>();

            services.AddSingleton<DependenciesCollectorOptions>(new DependenciesCollectorOptions());
            services.AddSingleton<DependenciesCollector>();
        }

        public void ConfigureOpenTelemetry(IServiceProvider serviceProvider)
        {
            serviceProvider.GetRequiredService<ILogger<Startup>>().LogInformation("Enabling OpenTelemetry");

            serviceProvider.GetRequiredService<RequestsCollector>();
            serviceProvider.GetRequiredService<DependenciesCollector>();
        }
    }
}
