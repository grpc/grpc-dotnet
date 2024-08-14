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

using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FunctionalTestsWebsite.Infrastructure;
using FunctionalTestsWebsite.Services;
using Greet;
using Grpc.AspNetCore.Server.Model;
using Grpc.HealthCheck;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace FunctionalTestsWebsite;

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
                options.MaxSendMessageSize = 64 * 1024;
                options.MaxReceiveMessageSize = 64 * 1024;
            })
            .AddServiceOptions<CompressionService>(options =>
            {
                options.ResponseCompressionAlgorithm = "gzip";
            });
        services.AddHttpContextAccessor();

        services
            .AddGrpcClient<Greeter.GreeterClient>((s, o) => { o.Address = GetCurrentAddress(s); })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator })
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
            });

        services.AddCors(o =>
        {
            o.AddPolicy("FunctionalTests", builder =>
            {
                builder.AllowAnyOrigin();
                builder.AllowAnyMethod();
                builder.AllowAnyHeader();
                builder.WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
            });
        });

        services.AddScoped<IncrementingCounter>();

        services.AddSingleton<SingletonValueProvider>();
        services.AddTransient<TransientValueProvider>();
        services.AddScoped<ScopedValueProvider>();

        // When the site is run from the test project these types will be injected
        // This will add a default types if the site is run standalone
        services.TryAddSingleton<DynamicEndpointDataSource>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceMethodProvider<DynamicService>, DynamicServiceModelProvider>());

        // Add a Singleton service
        services.AddSingleton<SingletonCounterService>();

        services.AddSingleton<HealthServiceImpl>(s =>
        {
            var service = new HealthServiceImpl();
            service.SetStatus("", Grpc.Health.V1.HealthCheckResponse.Types.ServingStatus.Serving);
            return service;
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

        services.Configure<RequestLocalizationOptions>(options =>
        {
            const string enUSCulture = "en-US";
            var supportedCultures = new[]
            {
                new CultureInfo(enUSCulture),
                new CultureInfo("fr")
            };

            options.DefaultRequestCulture = new RequestCulture(culture: enUSCulture, uiCulture: enUSCulture);
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app)
    {
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Startup));
        app.Use(async (context, next) =>
        {
            // Allow a call to specify a deadline, without enabling deadline timer on server.
            if (context.Request.Headers.TryGetValue("remove-deadline", out var value) &&
                bool.TryParse(value.ToString(), out var remove) && remove)
            {
                logger.LogInformation("Removing grpc-timeout header.");
                context.Request.Headers.Remove("grpc-timeout");
            }

            await next();
        });

        app.Use(async (context, next) =>
        {
            // Allow a call to specify activity tags are returned as trailers.
            if (context.Request.Headers.TryGetValue("return-tags-trailers", out var value) &&
                bool.TryParse(value.ToString(), out var remove) && remove)
            {
                logger.LogInformation("Replacing activity.");

                // Replace the activity to check that tags are added to the host activity.
                using (new ActivityReplacer("GrpcFunctionalTests"))
                {
                    await next();
                }

                logger.LogInformation("Adding tags to trailers.");

                foreach (var tag in Activity.Current!.Tags)
                {
                    context.Response.AppendTrailer(tag.Key, tag.Value);
                }
            }
            else
            {
                await next();
            }
        });
        app.UseRouting();

        app.UseRequestLocalization();

        app.UseAuthorization();
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
        app.UseCors();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGrpcService<SecondGreeterService>().RequireCors("FunctionalTests");
            endpoints.MapGrpcService<ChatterService>();
            endpoints.MapGrpcService<CounterService>();
            endpoints.MapGrpcService<AuthorizedGreeter>();
            endpoints.MapGrpcService<LifetimeService>();
            endpoints.MapGrpcService<SingletonCounterService>();
            endpoints.MapGrpcService<NestedService>();
            endpoints.MapGrpcService<CompressionService>();
            endpoints.MapGrpcService<AnyService>();
            endpoints.MapGrpcService<GreeterService>();
            endpoints.MapGrpcService<StreamService>();
            endpoints.MapGrpcService<RacerService>();
            endpoints.MapGrpcService<EchoService>();
            endpoints.MapGrpcService<IssueService>();
            endpoints.MapGrpcService<TesterService>();
            endpoints.MapGrpcService<HealthServiceImpl>();

            endpoints.DataSources.Add(endpoints.ServiceProvider.GetRequiredService<DynamicEndpointDataSource>());

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
    private readonly SymmetricSecurityKey SecurityKey = new SymmetricSecurityKey(new byte[256]);
}
