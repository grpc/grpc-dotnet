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
using Google.Protobuf.WellKnownTypes;
using Greet;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace BenchmarkServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddGrpc();
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<GreeterService>();

                endpoints.MapPost("/raw/greeter", async context =>
                {
                    MemoryStream ms = new MemoryStream();
                    await context.Request.Body.CopyToAsync(ms);
                    ms.Seek(0, SeekOrigin.Begin);

                    JsonSerializer serializer = new JsonSerializer();
                    var message = serializer.Deserialize<HelloRequest>(new JsonTextReader(new StreamReader(ms)));

                    ms.Seek(0, SeekOrigin.Begin);
                    using (var writer = new JsonTextWriter(new StreamWriter(ms, Encoding.UTF8, 1024, true)))
                    {
                        serializer.Serialize(writer, new HelloReply { Message = "Hello " + message.Name, Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) });
                    }

                    context.Response.StatusCode = StatusCodes.Status200OK;

                    ms.Seek(0, SeekOrigin.Begin);
                    await ms.CopyToAsync(context.Response.Body);
                });

                endpoints.MapControllers();
            });
        }
    }
}
