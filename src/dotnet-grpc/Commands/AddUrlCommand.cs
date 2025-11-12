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
    public AddUrlCommand(ConsoleService console, string? projectPath, HttpClient httpClient)
        : base(console, projectPath, httpClient) { }

    // Internal for testing
    internal AddUrlCommand(ConsoleService console, HttpClient client)
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
        var outputOption = new Option<string>("--output", ["-o"])
        {
            Description = CoreStrings.OutputOptionDescription
        };
        var urlArgument = new Argument<string>("url")
        {
            Description = CoreStrings.AddUrlCommandArgumentDescription,
            Arity = ArgumentArity.ExactlyOne
        };

        command.Add(outputOption);
        command.Add(projectOption);
        command.Add(serviceOption);
        command.Add(additionalImportDirsOption);
        command.Add(accessOption);
        command.Add(urlArgument);

        command.SetAction(
            async (context) =>
            {
                var project = context.GetValue(projectOption);
                var services = context.GetValue(serviceOption);
                var access = context.GetValue(accessOption);
                var additionalImportDirs = context.GetValue(additionalImportDirsOption);
                var output = context.GetValue(outputOption);
                var url = context.GetRequiredValue(urlArgument);

                var console = new ConsoleService(context.InvocationConfiguration.Output, context.InvocationConfiguration.Error);
                try
                {
                    if (string.IsNullOrEmpty(output))
                    {
                        throw new CLIToolException(CoreStrings.ErrorNoOutputProvided);
                    }

                    var command = new AddUrlCommand(console, project, httpClient);
                    await command.AddUrlAsync(services, access, additionalImportDirs, url, output);

                    return 0;
                }
                catch (CLIToolException e)
                {
                    console.LogError(e);

                    return -1;
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
