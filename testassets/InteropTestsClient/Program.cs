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
using System.CommandLine.Invocation;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Grpc.Shared.TestAssets;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InteropTestsClient;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = new List<Option>();
        var clientTypeOption = new Option<string>("--client_type") { DefaultValueFactory = (r) => "httpclient", HelpName = nameof(ClientOptions.ClientType) };
        var serverHostOption = new Option<string>("--server_host") { Required = true, HelpName = nameof(ClientOptions.ServerHost) };
        var serverHostOverrideOption = new Option<string>("--server_host_override") { HelpName = nameof(ClientOptions.ServerHostOverride) };
        var serverPortOption = new Option<int>("--server_port") { Required = true, HelpName = nameof(ClientOptions.ServerPort) };
        var testCaseOption = new Option<string>("--test_case") { Required = true, HelpName = nameof(ClientOptions.TestCase) };
        var useTlsOption = new Option<bool>("--use_tls") { HelpName = nameof(ClientOptions.UseTls) };
        var useTestCAOption = new Option<bool>("--use_test_ca") { HelpName = nameof(ClientOptions.UseTestCa) };
        var defaultServiceAccountOption = new Option<string>("--default_service_account") { HelpName = nameof(ClientOptions.DefaultServiceAccount) };
        var oauthScopeOption = new Option<string>("--oauth_scope") { HelpName = nameof(ClientOptions.OAuthScope) };
        var serviceAccountKeyFileOption = new Option<string>("--service_account_key_file") { HelpName = nameof(ClientOptions.ServiceAccountKeyFile) };
        var grpcWebModeOption = new Option<string>("--grpc_web_mode") { HelpName = nameof(ClientOptions.GrpcWebMode) };
        var useWinHttpOption = new Option<bool>("--use_winhttp") { HelpName = nameof(ClientOptions.UseWinHttp) };
        var useHttp3Option = new Option<bool>("--use_http3") { HelpName = nameof(ClientOptions.UseHttp3) };

        var rootCommand = new RootCommand();
        rootCommand.Add(clientTypeOption);
        rootCommand.Add(serverHostOption);
        rootCommand.Add(serverHostOverrideOption);
        rootCommand.Add(serverPortOption);
        rootCommand.Add(testCaseOption);
        rootCommand.Add(useTlsOption);
        rootCommand.Add(useTestCAOption);
        rootCommand.Add(defaultServiceAccountOption);
        rootCommand.Add(oauthScopeOption);
        rootCommand.Add(serviceAccountKeyFileOption);
        rootCommand.Add(grpcWebModeOption);
        rootCommand.Add(useWinHttpOption);
        rootCommand.Add(useHttp3Option);

        rootCommand.SetAction(async (ParseResult context) =>
        {
            var options = new ClientOptions();
            options.ClientType = context.GetValue(clientTypeOption);
            options.ServerHost = context.GetValue(serverHostOption);
            options.ServerHostOverride = context.GetValue(serverHostOverrideOption);
            options.ServerPort = context.GetValue(serverPortOption);
            options.TestCase = context.GetValue(testCaseOption);
            options.UseTls = context.GetValue(useTlsOption);
            options.UseTestCa = context.GetValue(useTestCAOption);
            options.DefaultServiceAccount = context.GetValue(defaultServiceAccountOption);
            options.OAuthScope = context.GetValue(oauthScopeOption);
            options.ServiceAccountKeyFile = context.GetValue(serviceAccountKeyFileOption);
            options.GrpcWebMode = context.GetValue(grpcWebModeOption);
            options.UseWinHttp = context.GetValue(useWinHttpOption);
            options.UseHttp3 = context.GetValue(useHttp3Option);

            var runtimeVersion = typeof(object).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";

            Log("Runtime: " + runtimeVersion);

            using var serviceProvider = CreateServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            using var httpEventListener = new HttpEventSourceListener(loggerFactory);

            var interopClient = new InteropClient(options, loggerFactory);
            await interopClient.Run();

            // Pause to ensure all logs are flushed.
            await Task.Delay(TimeSpan.FromSeconds(0.1));
        });

        Log("Interop Test Client");

        var result = rootCommand.Parse(args);
        return await result.InvokeAsync();
    }

#if NET5_0_OR_GREATER
    [UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode",
       Justification = "App's DependencyInjection usage is safe.")]
#endif
    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(configure =>
        {
            configure.SetMinimumLevel(LogLevel.Trace);
            configure.AddSimpleConsole(loggerOptions => loggerOptions.IncludeScopes = true);
        });

        return services.BuildServiceProvider();
    }

    private static void Log(string message)
    {
        var time = DateTime.Now.ToString("hh:mm:ss.fff", CultureInfo.InvariantCulture);
        Console.WriteLine($"[{time}] {message}");
    }
}
