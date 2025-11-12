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

internal sealed class AddFileCommand : CommandBase
{
    public AddFileCommand(ConsoleService console, string? projectPath, HttpClient httpClient)
        : base(console, projectPath, httpClient) { }

    public static Command Create(HttpClient httpClient)
    {
        var command = new Command(
            name: "add-file",
            description: CoreStrings.AddFileCommandDescription);

        var projectOption = CommonOptions.ProjectOption();
        var serviceOption = CommonOptions.ServiceOption();
        var additionalImportDirsOption = CommonOptions.AdditionalImportDirsOption();
        var accessOption = CommonOptions.AccessOption();
        var filesArgument = new Argument<string[]>("files")
        {
            Description = CoreStrings.AddFileCommandArgumentDescription,
            Arity = ArgumentArity.OneOrMore
        };

        command.Add(projectOption);
        command.Add(serviceOption);
        command.Add(accessOption);
        command.Add(additionalImportDirsOption);
        command.Add(filesArgument);

        command.SetAction(
            async (context) =>
            {
                var project = context.GetValue(projectOption);
                var services = context.GetValue(serviceOption);
                var access = context.GetValue(accessOption);
                var additionalImportDirs = context.GetValue(additionalImportDirsOption);
                var files = context.GetValue(filesArgument) ?? [];

                var console = new ConsoleService(context.InvocationConfiguration.Output, context.InvocationConfiguration.Error);
                try
                {
                    var command = new AddFileCommand(console, project, httpClient);
                    await command.AddFileAsync(services, access, additionalImportDirs, files);

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

    public async Task AddFileAsync(Services services, Access access, string? additionalImportDirs, string[] files)
    {
        var resolvedServices = ResolveServices(services);
        await EnsureNugetPackagesAsync(resolvedServices);
        files = GlobReferences(files);

        foreach (var file in files)
        {
            Console.Log(CoreStrings.LogAddFileReference, file);
            AddProtobufReference(resolvedServices, additionalImportDirs, access, file, string.Empty);
        }

        Project.Save();
    }
}
