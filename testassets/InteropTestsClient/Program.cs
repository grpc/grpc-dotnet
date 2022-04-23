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

using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Grpc.Shared.TestAssets;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InteropTestsClient
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var options = new List<Option>();
            options.Add(new Option<string>(new string[] { "--client_type" }, () => "httpclient") { Name = nameof(ClientOptions.ClientType) });
            options.Add(new Option<string>(new string[] { "--server_host" }) { IsRequired = true, Name = nameof(ClientOptions.ServerHost) });
            options.Add(new Option<string>(new string[] { "--server_host_override" }) { Name = nameof(ClientOptions.ServerHostOverride) });
            options.Add(new Option<int>(new string[] { "--server_port" }) { IsRequired = true, Name = nameof(ClientOptions.ServerPort) });
            options.Add(new Option<string>(new string[] { "--test_case" }) { IsRequired = true, Name = nameof(ClientOptions.TestCase) });
            options.Add(new Option<bool>(new string[] { "--use_tls" }) { Name = nameof(ClientOptions.UseTls) });
            options.Add(new Option<bool>(new string[] { "--use_test_ca" }) { Name = nameof(ClientOptions.UseTestCa) });
            options.Add(new Option<string>(new string[] { "--default_service_account" }) { Name = nameof(ClientOptions.DefaultServiceAccount) });
            options.Add(new Option<string>(new string[] { "--oauth_scope" }) { Name = nameof(ClientOptions.OAuthScope) });
            options.Add(new Option<string>(new string[] { "--service_account_key_file" }) { Name = nameof(ClientOptions.ServiceAccountKeyFile) });
            options.Add(new Option<string>(new string[] { "--grpc_web_mode" }) { Name = nameof(ClientOptions.GrpcWebMode) });
            options.Add(new Option<bool>(new string[] { "--use_winhttp" }) { Name = nameof(ClientOptions.UseWinHttp) });
            options.Add(new Option<bool>(new string[] { "--use_http3" }) { Name = nameof(ClientOptions.UseHttp3) });

            var rootCommand = new RootCommand();
            foreach (var option in options)
            {
                rootCommand.AddOption(option);
            }

            rootCommand.SetHandler<ClientOptions>(async (options) =>
            {
                var runtimeVersion = typeof(object).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";

                Log("Runtime: " + runtimeVersion);

                var services = new ServiceCollection();
                services.AddLogging(configure =>
                {
                    configure.SetMinimumLevel(LogLevel.Trace);
                    configure.AddConsole(loggerOptions => loggerOptions.IncludeScopes = true);
                });

                using var serviceProvider = services.BuildServiceProvider();
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

                using var httpEventListener = new HttpEventSourceListener(loggerFactory);

                var interopClient = new InteropClient(options, loggerFactory);
                await interopClient.Run();
            }, new ReflectionBinder<ClientOptions>(options));

            Log("Interop Test Client");

            return await rootCommand.InvokeAsync(args);
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff", CultureInfo.InvariantCulture);
            Console.WriteLine($"[{time}] {message}");
        }

        private class ReflectionBinder<T> : BinderBase<T> where T : new()
        {
            private readonly List<Option> _options;

            public ReflectionBinder(List<Option> options)
            {
                _options = options;
            }

            protected override T GetBoundValue(BindingContext bindingContext)
            {
                var boundValue = new T();

                Log($"Binding {typeof(T)}");

                foreach (var option in _options)
                {
                    var value = bindingContext.ParseResult.GetValueForOption(option);

                    var propertyInfo = typeof(T).GetProperty(option.Name, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                    if (propertyInfo != null)
                    {
                        propertyInfo.SetValue(boundValue, value);

                        Log($"-{propertyInfo.Name} = {value}");
                    }
                }

                return boundValue;
            }
        }
    }
}
