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
using Frontend.Balancer;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frontend
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddServerSideBlazor();

            services.AddSingleton(services =>
            {
                var backendUrl = Configuration["BackendUrl"]!;

                var channel = GrpcChannel.ForAddress(backendUrl, new GrpcChannelOptions
                {
                    Credentials = ChannelCredentials.Insecure,
                    ServiceProvider = services
                });

                return channel;
            });

            SetupReportingServices(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
            });
        }

        private static void SetupReportingServices(IServiceCollection services)
        {
            // These services allow the load balancer policy to be configured and subchannels to be reported in the UI.
            services.AddSingleton<SubchannelReporter>();
            services.AddSingleton<BalancerConfiguration>();
            services.AddSingleton<ResolverFactory>(s =>
            {
                var inner = new DnsResolverFactory(refreshInterval: TimeSpan.FromSeconds(20));
                return new ConfigurableResolverFactory(inner, s.GetRequiredService<BalancerConfiguration>());
            });
            services.AddSingleton<LoadBalancerFactory>(s =>
            {
                var inner = new RoundRobinBalancerFactory();
                return new ReportingLoadBalancerFactory(inner, s.GetRequiredService<SubchannelReporter>());
            });
            services.AddSingleton<LoadBalancerFactory>(s =>
            {
                var inner = new PickFirstBalancerFactory();
                return new ReportingLoadBalancerFactory(inner, s.GetRequiredService<SubchannelReporter>());
            });
        }
    }
}
