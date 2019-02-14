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

using System.Runtime.InteropServices;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace InteropTestsWebsite
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args)
        {
            var builder = WebHost.CreateDefaultBuilder(args);

            // Support --port and --use_tls cmdline arguments normally supported
            // by gRPC interop servers.
            int port = int.Parse(builder.GetSetting("port") ?? "50052");
            bool useTls = bool.Parse(builder.GetSetting("use_tls") ?? "false");

            builder.ConfigureKestrel(options =>
            {
                options.Limits.MinRequestBodyDataRate = null;
                options.ListenAnyIP(port, listenOptions =>
                {
                    if (useTls)
                    {
                        listenOptions.UseHttps(Resources.ServerPFXPath, "1111");
                    }
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
            })
            .UseStartup<Startup>();
            return builder;
        }
    }
}
