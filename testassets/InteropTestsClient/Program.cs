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
        var clientTypeOption = new Option<string>(new string[] { "--client_type" }, () => "httpclient") { Name = nameof(ClientOptions.ClientType) };
        var serverHostOption = new Option<string>(new string[] { "--server_host" }) { IsRequired = true, Name = nameof(ClientOptions.ServerHost) };
        var serverHostOverrideOption = new Option<string>(new string[] { "--server_host_override" }) { Name = nameof(ClientOptions.ServerHostOverride) };
        var serverPortOption = new Option<int>(new string[] { "--server_port" }) { IsRequired = true, Name = nameof(ClientOptions.ServerPort) };
        var testCaseOption = new Option<string>(new string[] { "--test_case" }) { IsRequired = true, Name = nameof(ClientOptions.TestCase) };
        var useTlsOption = new Option<bool>(new string[] { "--use_tls" }) { Name = nameof(ClientOptions.UseTls) };
        var useTestCAOption = new Option<bool>(new string[] { "--use_test_ca" }) { Name = nameof(ClientOptions.UseTestCa) };
        var defaultServiceAccountOption = new Option<string>(new string[] { "--default_service_account" }) { Name = nameof(ClientOptions.DefaultServiceAccount) };
        var oauthScopeOption = new Option<string>(new string[] { "--oauth_scope" }) { Name = nameof(ClientOptions.OAuthScope) };
        var serviceAccountKeyFileOption = new Option<string>(new string[] { "--service_account_key_file" }) { Name = nameof(ClientOptions.ServiceAccountKeyFile) };
        var grpcWebModeOption = new Option<string>(new string[] { "--grpc_web_mode" }) { Name = nameof(ClientOptions.GrpcWebMode) };
        var useWinHttpOption = new Option<bool>(new string[] { "--use_winhttp" }) { Name = nameof(ClientOptions.UseWinHttp) };
        var useHttp3Option = new Option<bool>(new string[] { "--use_http3" }) { Name = nameof(ClientOptions.UseHttp3) };

        var rootCommand = new RootCommand();
        rootCommand.AddOption(clientTypeOption);
        rootCommand.AddOption(serverHostOption);
        rootCommand.AddOption(serverHostOverrideOption);
        rootCommand.AddOption(serverPortOption);
        rootCommand.AddOption(testCaseOption);
        rootCommand.AddOption(useTlsOption);
        rootCommand.AddOption(useTestCAOption);
        rootCommand.AddOption(defaultServiceAccountOption);
        rootCommand.AddOption(oauthScopeOption);
        rootCommand.AddOption(serviceAccountKeyFileOption);
        rootCommand.AddOption(grpcWebModeOption);
        rootCommand.AddOption(useWinHttpOption);
        rootCommand.AddOption(useHttp3Option);

        rootCommand.SetHandler(async (InvocationContext context) =>
        {
            var options = new ClientOptions();
            options.ClientType = context.ParseResult.GetValueForOption(clientTypeOption);
            options.ServerHost = context.ParseResult.GetValueForOption(serverHostOption);
            options.ServerHostOverride = context.ParseResult.GetValueForOption(serverHostOverrideOption);
            options.ServerPort = context.ParseResult.GetValueForOption(serverPortOption);
            options.TestCase = context.ParseResult.GetValueForOption(testCaseOption);
            options.UseTls = context.ParseResult.GetValueForOption(useTlsOption);
            options.UseTestCa = context.ParseResult.GetValueForOption(useTestCAOption);
            options.DefaultServiceAccount = context.ParseResult.GetValueForOption(defaultServiceAccountOption);
            options.OAuthScope = context.ParseResult.GetValueForOption(oauthScopeOption);
            options.ServiceAccountKeyFile = context.ParseResult.GetValueForOption(serviceAccountKeyFileOption);
            options.GrpcWebMode = context.ParseResult.GetValueForOption(grpcWebModeOption);
            options.UseWinHttp = context.ParseResult.GetValueForOption(useWinHttpOption);
            options.UseHttp3 = context.ParseResult.GetValueForOption(useHttp3Option);

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

        return await rootCommand.InvokeAsync(args);
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
