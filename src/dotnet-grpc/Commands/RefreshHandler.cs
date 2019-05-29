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

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Options;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands
{
    internal class RefreshHandler : HandlerBase
    {
        public static Command RefreshCommand()
        {
            var command = new Command(
                name: "refresh",
                description: "Check remote protobuf file(s) for updates and replace them if a newer version is available. If no file or url is provided, all remote protobuf files will be updated.",
                argument: new Argument<string[]>
                {
                    Name = "references",
                    Description = "The URL(s) or file path(s) to remote protobuf file(s) that should be updated.",
                    Arity = ArgumentArity.ZeroOrMore
                });

            command.AddOption(CommonOptions.ProjectOption());
            command.AddOption(new Option(
                aliases: new[] { "--dry-run" },
                description: "Obtain a list of file(s) that will be updated.",
                argument: Argument.None
                ));

            command.Handler = CommandHandler.Create<IConsole, FileInfo, bool, string[]>(new RefreshHandler().Refresh);

            return command;
        }

        public async Task Refresh(IConsole console, FileInfo? project, bool dryRun, string[] references)
        {
            Console = console;
            ResolveProject(project);

            if (Project == null)
            {
                throw new InvalidOperationException("Internal error: Project not set.");
            }

            var protobufItems = Project.GetItems("Protobuf");
            var refsToRefresh = new List<ProjectItem>();
            references = ExpandReferences(references);

            if (references.Length == 0)
            {
                refsToRefresh.AddRange(protobufItems.Where(p => p.HasMetadata("SourceURL")));
            }
            else
            {
                foreach (var reference in references)
                {
                    ProjectItem protobufRef;
                    if (IsUrl(reference))
                    {
                        protobufRef = protobufItems.SingleOrDefault(p => p.GetMetadataValue("SourceURL") == reference);

                        if (protobufRef == null)
                        {
                            Console.Out.WriteLine($"Could not find a reference that uses the source url `{reference}`.");
                            continue;
                        }
                    }
                    else
                    {
                        protobufRef = protobufItems.SingleOrDefault(p => p.UnevaluatedInclude == reference);

                        if (protobufRef == null)
                        {
                            Console.Out.WriteLine($"Could not find a reference for the file `{reference}`.");
                            continue;
                        }
                    }
                    refsToRefresh.Add(protobufRef);
                }
            }

            foreach (var reference in refsToRefresh)
            {
                await DownloadFileAsync(reference.GetMetadataValue("SourceURL"), reference.UnevaluatedInclude, true, dryRun);
            }
        }
    }
}
