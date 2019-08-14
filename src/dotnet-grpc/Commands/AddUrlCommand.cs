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
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Dotnet.Cli.Properties;

namespace Grpc.Dotnet.Cli.Commands
{
    internal class AddUrlCommand : CommandBase
    {
        public AddUrlCommand(IConsole console, FileInfo? projectPath)
            : base(console, projectPath) { }

        // Internal for testing
        internal AddUrlCommand(IConsole console, HttpClient client)
            : base(console, client) { }

        public static Command Create()
        {
            var command = new Command(
                name: "add-url",
                description: CoreStrings.AddUrlCommandDescription);
            command.AddArgument(new Argument<string>
            {
                Name = "url",
                Description = CoreStrings.AddUrlCommandArgumentDescription,
                Arity = ArgumentArity.ExactlyOne
            });

            var outputOption = new Option(
                aliases: new[] { "-o", "--output" },
                description: CoreStrings.OutputOptionDescription);
            outputOption.Argument = new Argument<string> { Name = "path", Arity = ArgumentArity.ExactlyOne };
            command.AddOption(outputOption);
            command.AddOption(CommonOptions.ProjectOption());
            command.AddOption(CommonOptions.ServiceOption());
            command.AddOption(CommonOptions.AdditionalImportDirsOption());
            command.AddOption(CommonOptions.AccessOption());

            command.Handler = CommandHandler.Create<IConsole, FileInfo, Services, Access, string, string, string>(
                async (console, project, services, access, additionalImportDirs, url, output) =>
                {
                    try
                    {
                        if (string.IsNullOrEmpty(output))
                        {
                            throw new CLIToolException(CoreStrings.ErrorNoOutputProvided);
                        }

                        var command = new AddUrlCommand(console, project);
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

        public async Task AddUrlAsync(Services services, Access access, string additionalImportDirs, string url, string output)
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
}
