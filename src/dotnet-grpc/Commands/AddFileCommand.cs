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
using System.IO;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Dotnet.Cli.Properties;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands
{
    internal class AddFileCommand : CommandBase
    {
        public AddFileCommand(IConsole console, FileInfo? projectPath)
            : base(console, projectPath) { }

        public static Command Create()
        {
            var command = new Command(
                name: "add-file",
                description: CoreStrings.AddFileCommandDescription);
            command.AddArgument(new Argument<string[]>
            {
                Name = "files",
                Description = CoreStrings.AddFileCommandArgumentDescription,
                Arity = ArgumentArity.OneOrMore
            });

            command.AddOption(CommonOptions.ProjectOption());
            command.AddOption(CommonOptions.ServiceOption());
            command.AddOption(CommonOptions.AdditionalImportDirsOption());
            command.AddOption(CommonOptions.AccessOption());

            command.Handler = CommandHandler.Create<IConsole, FileInfo, Services, Access, string, string[]>(
                async (console, project, services, access, additionalImportDirs, files) =>
                {
                    try
                    {
                        var command = new AddFileCommand(console, project);
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

        public async Task AddFileAsync(Services services, Access access, string additionalImportDirs, string[] files)
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
}
