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

using System.Security.Cryptography.X509Certificates;
using System.Text;
using Grpc.Shared;
using Grpc.Testing;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Authentication.Certificate;
using Newtonsoft.Json;

namespace GrpcAspNetCoreServer;

public class Startup
{
    private readonly IConfiguration _config;

    public Startup(IConfiguration config)
    {
        _config = config;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddGrpc(o =>
        {
            // Small performance benefit to not add catch-all routes to handle UNIMPLEMENTED for unknown services
            o.IgnoreUnknownServices = true;
        });
        services.Configure<RouteOptions>(c =>
        {
            // Small performance benefit to skip checking for security metadata on endpoint
            c.SuppressCheckForUnhandledSecurityMetadata = true;
        });
        services.AddSingleton<BenchmarkServiceImpl>();
        services.AddControllers();

        bool.TryParse(_config["enableCertAuth"], out var enableCertAuth);
        if (enableCertAuth)
        {
            services.AddAuthorization();
            services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme)
                .AddCertificate(options =>
                {
                    // Not recommended in production environments. The example is using a self-signed test certificate.
                    options.RevocationMode = X509RevocationMode.NoCheck;
                    options.AllowedCertificateTypes = CertificateTypes.All;
                });
        }
    }

    public void Configure(IApplicationBuilder app, IHostApplicationLifetime applicationLifetime)
    {
        // Required to notify performance infrastructure that it can begin benchmarks
        applicationLifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started."));

        var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
        if (loggerFactory.CreateLogger<Startup>().IsEnabled(LogLevel.Trace))
        {
            _ = new HttpEventSourceListener(loggerFactory);
        }

        app.UseRouting();

        bool.TryParse(_config["enableCertAuth"], out var enableCertAuth);
        if (enableCertAuth)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

#if GRPC_WEB
        bool.TryParse(_config["enableGrpcWeb"], out var enableGrpcWeb);

        if (enableGrpcWeb)
        {
            app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
        }
#endif

        app.UseMiddleware<ServiceProvidersMiddleware>();

        app.UseEndpoints(endpoints =>
        {
            ConfigureAuthorization(endpoints.MapGrpcService<BenchmarkServiceImpl>());

            ConfigureAuthorization(endpoints.MapControllers());

            ConfigureAuthorization(endpoints.MapGet("/", context =>
            {
                return context.Response.WriteAsync("Benchmark Server");
            }));

            ConfigureAuthorization(endpoints.MapPost("/unary", async context =>
            {
                MemoryStream ms = new MemoryStream();
                await context.Request.Body.CopyToAsync(ms);
                ms.Seek(0, SeekOrigin.Begin);

                JsonSerializer serializer = new JsonSerializer();
                var message = serializer.Deserialize<SimpleRequest>(new JsonTextReader(new StreamReader(ms)))!;

                ms.Seek(0, SeekOrigin.Begin);
                using (var writer = new JsonTextWriter(new StreamWriter(ms, Encoding.UTF8, 1024, true)))
                {
                    serializer.Serialize(writer, BenchmarkServiceImpl.CreateResponse(message));
                }

                context.Response.StatusCode = StatusCodes.Status200OK;

                ms.Seek(0, SeekOrigin.Begin);
                await ms.CopyToAsync(context.Response.Body);
            }));
        });
    }

    private void ConfigureAuthorization(IEndpointConventionBuilder builder)
    {
        bool.TryParse(_config["enableCertAuth"], out var enableCertAuth);
        if (enableCertAuth)
        {
            builder.RequireAuthorization();
        }
    }
}
