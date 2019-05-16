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
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Server.Interceptors;

namespace GRPCServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddGrpc(options =>
                {
                    // This registers a global interceptor with a Singleton lifetime. The interceptor must be added to the service collection in addition to being registered here.
                    options.Interceptors.Add<MaxConcurrentCallsInterceptor>();
                    // This registers a global interceptor with a Scoped lifetime.
                    options.Interceptors.Add<MaxStreamingRequestTimeoutInterceptor>(TimeSpan.FromSeconds(30));
                })
                .AddServiceOptions<GreeterService>(options =>
                {
                    // This registers an interceptor for the Greeter service with a Singleton lifetime.
                    // NOTE: Not all calls should be cached. Since the response of this service only depends on the request and no other state, adding caching here is acceptable.
                    options.Interceptors.Add<UnaryCachingInterceptor>();
                });
            services.AddGrpcReflection();
            services.AddSingleton(new MaxConcurrentCallsInterceptor(200));
            services.AddSingleton<UnaryCachingInterceptor>();
            services.AddSingleton<IncrementingCounter>();
            services.AddSingleton<MailQueueRepository>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<MailerService>();
                endpoints.MapGrpcService<CounterService>();
                endpoints.MapGrpcService<GreeterService>();
                //endpoints.MapGrpcReflectionService();
            });
        }
    }
}