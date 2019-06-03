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
using System.Threading.Tasks;
using FunctionalTestsWebsite.Infrastructure;
using FunctionalTestsWebsite.Services;
using Greet;
using Grpc.AspNetCore.Server.Model;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace FunctionalTestsWebsite
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
                    options.EnableDetailedErrors = true;
                })
                .AddServiceOptions<GreeterService>(options =>
                {
                    options.SendMaxMessageSize = 64 * 1024;
                    options.ReceiveMaxMessageSize = 64 * 1024;
                })
                .AddServiceOptions<CompressionService>(options =>
                {
                    options.ResponseCompressionAlgorithm = "gzip";
                });
            services.AddHttpContextAccessor();

            services
                .AddGrpcClient<Greeter.GreeterClient>(options => options.BaseAddress = new Uri("https://localhost:8080"))
                .UsePrimaryMessageHandlerProvider();

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
                });

            services.AddScoped<IncrementingCounter>();

            services.AddSingleton<SingletonValueProvider>();
            services.AddTransient<TransientValueProvider>();
            services.AddScoped<ScopedValueProvider>();

            // When the site is run from the test project these types will be injected
            // This will add a default types if the site is run standalone
            services.TryAddSingleton<IPrimaryMessageHandlerProvider, HttpPrimaryMessageHandlerProvider>();
            services.TryAddSingleton<DynamicEndpointDataSource>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceMethodProvider<DynamicService>, DynamicServiceModelProvider>());

            // Add a Singleton service
            services.AddSingleton<SingletonCounterService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                // Bind via reflection
                endpoints.MapGrpcService<ChatterService>();
                endpoints.MapGrpcService<CounterService>();
                endpoints.MapGrpcService<AuthorizedGreeter>();
                endpoints.MapGrpcService<SecondGreeterService>();
                endpoints.MapGrpcService<LifetimeService>();
                endpoints.MapGrpcService<SingletonCounterService>();
                endpoints.MapGrpcService<NestedService>();
                endpoints.MapGrpcService<CompressionService>();
                endpoints.MapGrpcService<AnyService>();
                endpoints.MapGrpcService<GreeterService>();

                endpoints.DataSources.Add(endpoints.ServiceProvider.GetRequiredService<DynamicEndpointDataSource>());

                endpoints.Map("{FirstSegment}/{SecondSegment}", context =>
                {
                    context.Response.StatusCode = StatusCodes.Status418ImATeapot;
                    return Task.CompletedTask;
                });

                endpoints.MapGet("/generateJwtToken", context =>
                {
                    return context.Response.WriteAsync(GenerateJwtToken());
                });
            });
        }

        private string GenerateJwtToken()
        {
            var claims = new[] { new Claim(ClaimTypes.Name, "testuser") };
            var credentials = new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken("FunctionalTestServer", "FunctionalTests", claims, expires: DateTime.Now.AddSeconds(5), signingCredentials: credentials);
            return JwtTokenHandler.WriteToken(token);
        }

        private readonly JwtSecurityTokenHandler JwtTokenHandler = new JwtSecurityTokenHandler();
        private readonly SymmetricSecurityKey SecurityKey = new SymmetricSecurityKey(Guid.NewGuid().ToByteArray());
    }
}
