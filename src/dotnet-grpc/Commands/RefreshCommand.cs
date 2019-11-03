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
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Dotnet.Cli.Properties;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands
{
    internal class RefreshCommand : CommandBase
    {
        public RefreshCommand(IConsole console, FileInfo? projectPath)
            : base(console, projectPath) { }

        // Internal for testing
        public RefreshCommand(IConsole console, HttpClient client)
            : base(console, client) { }

        public static Command Create()
        {
            var command = new Command(
                name: "refresh",
                description: CoreStrings.RefreshCommandDescription);

            command.AddArgument(new Argument<string[]>
            {
                Name = "references",
                Description = CoreStrings.RefreshCommandArgumentDescription,
                Arity = ArgumentArity.ZeroOrMore
            });
            command.AddOption(CommonOptions.ProjectOption());
            command.AddOption(new Option(
                aliases: new[] { "--dry-run" },
                description: CoreStrings.DryRunOptionDescription
                ));

            command.Handler = CommandHandler.Create<IConsole, FileInfo, bool, string[]>(
                async (console, project, dryRun, references) =>
                {
                    try
                    {
                        var command = new RefreshCommand(console, project);
                        await command.RefreshAsync(dryRun, references);

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

        public async Task RefreshAsync(bool dryRun, string[] references)
        {
            var refsToRefresh = references.Length == 0 ? Project.GetItems(ProtobufElement).Where(p => p.HasMetadata(SourceUrlElement)) : ResolveReferences(references);

            foreach (var reference in refsToRefresh)
            {
                if (!reference.HasMetadata(SourceUrlElement))
                {
                    continue;
                }

                await DownloadFileAsync(reference.GetMetadataValue(SourceUrlElement), reference.UnevaluatedInclude, dryRun);
            }
        }
    }
}
