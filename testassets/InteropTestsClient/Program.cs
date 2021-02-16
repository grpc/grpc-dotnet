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
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;
using System.Threading.Tasks;
using Grpc.Shared.TestAssets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InteropTestsClient
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand();
            rootCommand.AddOption(new Option<string>(new string[] { "--client_type", nameof(ClientOptions.ClientType) }, () => "httpclient"));
            rootCommand.AddOption(new Option<string>(new string[] { "--server_host", nameof(ClientOptions.ServerHost) }) { Required = true });
            rootCommand.AddOption(new Option<string>(new string[] { "--server_host_override", nameof(ClientOptions.ServerHostOverride) }));
            rootCommand.AddOption(new Option<int>(new string[] { "--server_port", nameof(ClientOptions.ServerPort) }) { Required = true });
            rootCommand.AddOption(new Option<string>(new string[] { "--test_case", nameof(ClientOptions.TestCase) }) { Required = true });
            rootCommand.AddOption(new Option<bool>(new string[] { "--use_tls", nameof(ClientOptions.UseTls) }));
            rootCommand.AddOption(new Option<bool>(new string[] { "--use_test_ca", nameof(ClientOptions.UseTestCa) }));
            rootCommand.AddOption(new Option<string>(new string[] { "--default_service_account", nameof(ClientOptions.DefaultServiceAccount) }));
            rootCommand.AddOption(new Option<string>(new string[] { "--oauth_scope", nameof(ClientOptions.OAuthScope) }));
            rootCommand.AddOption(new Option<string>(new string[] { "--service_account_key_file", nameof(ClientOptions.ServiceAccountKeyFile) }));
            rootCommand.AddOption(new Option<string>(new string[] { "--grpc_web_mode", nameof(ClientOptions.GrpcWebMode) }));
            rootCommand.AddOption(new Option<bool>(new string[] { "--use_winhttp", nameof(ClientOptions.UseWinHttp) }));

            rootCommand.Handler = CommandHandler.Create<ClientOptions>(async (options) =>
            {
                var runtimeVersion = typeof(object).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";

                Console.WriteLine("Runtime: " + runtimeVersion);
                Console.WriteLine("Use TLS: " + options.UseTls);
                Console.WriteLine("Use WinHttp: " + options.UseWinHttp);
                Console.WriteLine("Use GrpcWebMode: " + options.GrpcWebMode);
                Console.WriteLine("Use Test CA: " + options.UseTestCa);
                Console.WriteLine("Client type: " + options.ClientType);
                Console.WriteLine("Server host: " + options.ServerHost);
                Console.WriteLine("Server port: " + options.ServerPort);

                var services = new ServiceCollection();
                services.AddLogging(configure =>
                {
                    configure.SetMinimumLevel(LogLevel.Trace);
                    configure.AddConsole(loggerOptions => loggerOptions.IncludeScopes = true);
                });

                using var serviceProvider = services.BuildServiceProvider();

                var interopClient = new InteropClient(options, serviceProvider.GetRequiredService<ILoggerFactory>());
                await interopClient.Run();
            });

            return await rootCommand.InvokeAsync(args);
        }
    }
}
