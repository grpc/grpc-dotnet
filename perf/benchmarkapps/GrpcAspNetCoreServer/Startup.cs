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
using System.IO;
using System.Text;
using Grpc.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
#if CLIENT_CERTIFICATE_AUTHENTICATION
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Certificate;
#endif

namespace GrpcAspNetCoreServer
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddGrpc(o =>
            {
                // Small performance benefits to not add catch-all routes to handle UNIMPLEMENTED for unknown services
                o.IgnoreUnknownServices = true;
            });
            services.AddControllers();

#if CLIENT_CERTIFICATE_AUTHENTICATION
            services.AddAuthorization();
            services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme)
                .AddCertificate(options =>
                {
                    // Not recommended in production environments. The example is using a self-signed test certificate.
                    options.RevocationMode = X509RevocationMode.NoCheck;
                    options.AllowedCertificateTypes = CertificateTypes.All;
                });
#endif
#if GRPC_WEB
            services.AddGrpcWeb(o => o.GrpcWebEnabled = true);
#endif
        }

        public void Configure(IApplicationBuilder app, IHostApplicationLifetime applicationLifetime)
        {
            // Required to notify performance infrastructure that it can begin benchmarks
            applicationLifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started."));

            app.UseRouting();

#if CLIENT_CERTIFICATE_AUTHENTICATION
            app.UseAuthentication();
            app.UseAuthorization();
#endif

#if GRPC_WEB
            app.UseGrpcWeb();
#endif

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<BenchmarkServiceImpl>();

                endpoints.MapControllers();
                
                endpoints.MapGet("/", context =>
                {
                    return context.Response.WriteAsync("Benchmark Server");
                });

                endpoints.MapPost("/unary", async context =>
                {
                    MemoryStream ms = new MemoryStream();
                    await context.Request.Body.CopyToAsync(ms);
                    ms.Seek(0, SeekOrigin.Begin);

                    JsonSerializer serializer = new JsonSerializer();
                    var message = serializer.Deserialize<SimpleRequest>(new JsonTextReader(new StreamReader(ms)));

                    ms.Seek(0, SeekOrigin.Begin);
                    using (var writer = new JsonTextWriter(new StreamWriter(ms, Encoding.UTF8, 1024, true)))
                    {
                        serializer.Serialize(writer, BenchmarkServiceImpl.CreateResponse(message));
                    }

                    context.Response.StatusCode = StatusCodes.Status200OK;

                    ms.Seek(0, SeekOrigin.Begin);
                    await ms.CopyToAsync(context.Response.Body);
                });
            });
        }
    }
}
