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
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Count;
using Greet;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
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
                })
                .AddServiceOptions<GreeterService>(options =>
                {
                    // This registers an interceptor for the Greeter service with a Singleton lifetime.
                    // NOTE: Not all calls should be cached. Since the response of this service only depends on the request and no other state, adding caching here is acceptable.
                    options.Interceptors.Add<UnaryCachingInterceptor>();
                    // This registers an interceptor for the Greeter service with a Scoped lifetime.
                    options.Interceptors.Add<MaxStreamingRequestTimeoutInterceptor>(TimeSpan.FromSeconds(30));
                });
            services.AddGrpcReflection();
            services.AddSingleton(new MaxConcurrentCallsInterceptor(200));
            services.AddSingleton<UnaryCachingInterceptor>();
            services.AddSingleton<IncrementingCounter>();
            services.AddSingleton<MailQueueRepository>();
            services.AddSingleton<TicketRepository>();

            // These clients will call back to the server
            services
                .AddGrpcClient<Greeter.GreeterClient>((s, o) => { o.BaseAddress = GetCurrentAddress(s); })
                .EnableCallContextPropagation();
            services
                .AddGrpcClient<Counter.CounterClient>((s, o) => { o.BaseAddress = GetCurrentAddress(s); })
                .EnableCallContextPropagation();

            services.AddAuthorization(options =>
            {
                options.AddPolicy(JwtBearerDefaults.AuthenticationScheme, policy =>
                {
                    policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                    policy.RequireClaim(ClaimTypes.Name);
                });
            });
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters =
                        new TokenValidationParameters
                        {
                            ValidateAudience = false,
                            ValidateIssuer = false,
                            ValidateActor = false,
                            ValidateLifetime = true,
                            IssuerSigningKey = SecurityKey
                        };
                })
                .AddCertificate(options =>
                {
                    // Not recommended in production environments. The example is using a test certificate
                    options.RevocationMode = X509RevocationMode.NoCheck;
                });

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

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<MailerService>();
                endpoints.MapGrpcService<CounterService>();
                endpoints.MapGrpcService<GreeterService>();
                endpoints.MapGrpcService<TicketerService>();
                endpoints.MapGrpcService<CertifierService>();
                endpoints.MapGrpcService<AggregatorService>();

                endpoints.MapGrpcReflectionService();

                endpoints.MapGet("/generateJwtToken", context =>
                {
                    return context.Response.WriteAsync(GenerateJwtToken(context.Request.Query["name"]));
                });
            });
        }

        private string GenerateJwtToken(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException("Name is not specified.");
            }

            var claims = new[] { new Claim(ClaimTypes.Name, name) };
            var credentials = new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken("ExampleServer", "ExampleClients", claims, expires: DateTime.Now.AddSeconds(60), signingCredentials: credentials);
            return JwtTokenHandler.WriteToken(token);
        }

        private readonly JwtSecurityTokenHandler JwtTokenHandler = new JwtSecurityTokenHandler();
        private readonly SymmetricSecurityKey SecurityKey = new SymmetricSecurityKey(Guid.NewGuid().ToByteArray());
    }
}
