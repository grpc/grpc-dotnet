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

namespace Grpc.Dotnet.Cli.Commands
{
    internal class AddUrlCommand : CommandBase
    {
        public static Command Create()
        {
            var command = new Command(
                name: "url",
                description: "Add a protobuf url reference to the gRPC project.",
                argument: new Argument<string>
                {
                    Name = "url",
                    Description = "The URL to a remote protobuf file.",
                });

            command.AddOption(CommonOptions.ProjectOption());
            command.AddOption(CommonOptions.ServiceOption());
            command.AddOption(CommonOptions.AdditionalImportDirsOption());
            command.AddOption(CommonOptions.AccessOption());
            command.AddOption(new Option(
                aliases: new[] { "-o", "--output" },
                description: "Specify the download path for the remote protobuf file. This is a required option.",
                argument: new Argument<string> { Name = "path", Arity = ArgumentArity.ExactlyOne }));

            command.Handler = CommandHandler.Create<IConsole, FileInfo, Services, Access, string, string, string>(new AddUrlCommand().AddUrl);

            return command;
        }

        public async Task<int> AddUrl(IConsole console, FileInfo? project, Services services, Access access, string additionalImportDirs, string url, string output)
        {
            Console = console;

            try
            {
                Project = ResolveProject(project);
                EnsureNugetPackages();

                await DownloadFileAsync(url, output);

                Console.Out.WriteLine($"Adding file reference {output} with content from {url}.");
                AddProtobufReference(services, additionalImportDirs, access, output, url);

                Project.Save();

                return 0;
            }
            catch (CLIToolException e)
            {
                Console.Error.WriteLine($"Error: {e.Message}");

                return -1;
            }
        }
    }
}
