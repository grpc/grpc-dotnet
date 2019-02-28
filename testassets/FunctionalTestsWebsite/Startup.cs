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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
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
                });
            services.AddHttpContextAccessor();

            services.AddAuthorization(options =>
            {
                options.AddPolicy(JwtBearerDefaults.AuthenticationScheme, policy =>
                {
                    policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                    policy.RequireClaim(ClaimTypes.NameIdentifier);
                });
            });
            services.AddAuthorizationPolicyEvaluator();
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

                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            var token = context.Request.Headers["access_token"];
                            if (!string.IsNullOrEmpty(token))
                            {
                                context.Token = token;
                            }
                            return Task.CompletedTask;
                        }
                    };
                });

            services.AddScoped<IncrementingCounter>();

            services.AddSingleton<SingletonValueProvider>();
            services.AddTransient<TransientValueProvider>();
            services.AddScoped<ScopedValueProvider>();

            // When the site is run from the test project a signaler will already be registered
            // This will add a default one if the site is run standalone
            services.TryAddSingleton<TrailersContainer>();

            services.TryAddSingleton<DynamicEndpointDataSource>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.Use((context, next) =>
            {
                // Workaround for https://github.com/aspnet/AspNetCore/issues/6880
                if (!context.Response.SupportsTrailers())
                {
                    context.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature
                    {
                        Trailers = new HttpResponseTrailers()
                    });
                }

                // Workaround for https://github.com/aspnet/AspNetCore/issues/7449
                context.Features.Set<IHttpRequestLifetimeFeature>(new TestHttpRequestLifetimeFeature());
                // Workaround for https://github.com/aspnet/AspNetCore/issues/7780
                context.Features.Set<IHttpResponseStartFeature>(new TestHttpResponseStartFeature());

                return next();
            });

            app.UseRouting(routes =>
            {
                // Bind via reflection
                routes.MapGrpcService<ChatterService>();
                routes.MapGrpcService<CounterService>();
				routes.MapGrpcService<AuthorizedGreeter>();
				routes.MapGrpcService<SecondGreeterService>();
				routes.MapGrpcService<LifetimeService>();

                // Bind via configure method
                routes.MapGrpcService<GreeterService>(options => options.BindAction = Greet.Greeter.BindService);

                routes.DataSources.Add(routes.ServiceProvider.GetRequiredService<DynamicEndpointDataSource>());

                routes.Map("{FirstSegment}/{SecondSegment}", context =>
                {
                    context.Response.StatusCode = StatusCodes.Status418ImATeapot;
                    return Task.CompletedTask;
                });

                routes.MapGet("/generateJwtToken", context =>
                {
                    return context.Response.WriteAsync(GenerateJwtToken());
                });
            });

            app.UseAuthorization();

            app.Use(async (context, next) =>
            {
                await next();

                var trailers = context.Features.Get<IHttpResponseTrailersFeature>().Trailers;

                var trailersContainer = context.RequestServices.GetRequiredService<TrailersContainer>();

                trailersContainer.Trailers.Clear();
                foreach (var trailer in trailers)
                {
                    trailersContainer.Trailers[trailer.Key] = trailer.Value;
                }
            });
        }

        private string GenerateJwtToken()
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "testuser") };
            var credentials = new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken("FunctionalTestServer", "FunctionalTests", claims, expires: DateTime.Now.AddSeconds(5), signingCredentials: credentials);
            return JwtTokenHandler.WriteToken(token);
        }

        private readonly JwtSecurityTokenHandler JwtTokenHandler = new JwtSecurityTokenHandler();
        private readonly SymmetricSecurityKey SecurityKey = new SymmetricSecurityKey(Guid.NewGuid().ToByteArray());
    }
}
