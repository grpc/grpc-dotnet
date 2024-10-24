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
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Dotnet.Cli.Properties;

namespace Grpc.Dotnet.Cli.Commands;

internal sealed class AddUrlCommand : CommandBase
{
    public AddUrlCommand(IConsole console, string? projectPath, HttpClient httpClient)
        : base(console, projectPath, httpClient) { }

    // Internal for testing
    internal AddUrlCommand(IConsole console, HttpClient client)
        : base(console, client) { }

    public static Command Create(HttpClient httpClient)
    {
        var command = new Command(
            name: "add-url",
            description: CoreStrings.AddUrlCommandDescription);

        var projectOption = CommonOptions.ProjectOption();
        var serviceOption = CommonOptions.ServiceOption();
        var additionalImportDirsOption = CommonOptions.AdditionalImportDirsOption();
        var accessOption = CommonOptions.AccessOption();
        var outputOption = new Option<string>(
            aliases: new[] { "-o", "--output" },
            description: CoreStrings.OutputOptionDescription);
        var urlArgument = new Argument<string>
        {
            Name = "url",
            Description = CoreStrings.AddUrlCommandArgumentDescription,
            Arity = ArgumentArity.ExactlyOne
        };

        command.AddOption(outputOption);
        command.AddOption(projectOption);
        command.AddOption(serviceOption);
        command.AddOption(additionalImportDirsOption);
        command.AddOption(accessOption);
        command.AddArgument(urlArgument);

        command.SetHandler(
            async (context) =>
            {
                var project = context.ParseResult.GetValueForOption(projectOption);
                var services = context.ParseResult.GetValueForOption(serviceOption);
                var access = context.ParseResult.GetValueForOption(accessOption);
                var additionalImportDirs = context.ParseResult.GetValueForOption(additionalImportDirsOption);
                var output = context.ParseResult.GetValueForOption(outputOption);
                var url = context.ParseResult.GetValueForArgument(urlArgument);

                try
                {
                    if (string.IsNullOrEmpty(output))
                    {
                        throw new CLIToolException(CoreStrings.ErrorNoOutputProvided);
                    }

                    var command = new AddUrlCommand(context.Console, project, httpClient);
                    await command.AddUrlAsync(services, access, additionalImportDirs, url, output);

                    context.ExitCode = 0;
                }
                catch (CLIToolException e)
                {
                    context.Console.LogError(e);

                    context.ExitCode = -1;
                }
            });

        return command;
    }

    public async Task AddUrlAsync(Services services, Access access, string? additionalImportDirs, string url, string output)
    {
        var resolvedServices = ResolveServices(services);
        await EnsureNugetPackagesAsync(resolvedServices);

        if (!IsUrl(url))
        {
            throw new CLIToolException(CoreStrings.ErrorReferenceNotUrl);
        }

        await DownloadFileAsync(url, output);

        Console.Log(CoreStrings.LogAddUrlReference, output, url);
        AddProtobufReference(resolvedServices, additionalImportDirs, access, output, url);

        Project.Save();
    }
}
