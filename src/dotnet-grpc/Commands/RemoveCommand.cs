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

using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands
{
    internal class RemoveCommand : CommandBase
    {
        public static Command Create()
        {
            var command = new Command(
                name: "remove",
                description: "Remove protobuf references(s).",
                argument: new Argument<string[]>
                {
                    Name = "references",
                    Description = "The URL(s) or file path(s) of the protobuf references to remove.",
                    Arity = ArgumentArity.OneOrMore
                });

            command.AddOption(CommonOptions.ProjectOption());
            command.AddOption(new Option(
                aliases: new[] { "--remove-file" },
                description: "Delete the protobuf file that was referenced from disk.",
                argument: Argument.None));

            command.Handler = CommandHandler.Create<IConsole, FileInfo, bool, string[]>(new RemoveCommand().Remove);

            return command;
        }

        public int Remove(IConsole console, FileInfo? project, bool removeFile, string[] references)
        {
            Console = console;

            try
            {
                Project = ResolveProject(project);

                var protobufItems = Project.GetItems("Protobuf");
                var refsToRefresh = new List<ProjectItem>();
                references = GlobReferences(references);

                foreach (var reference in references)
                {
                    ProjectItem protobufRef;

                    if (IsUrl(reference))
                    {
                        protobufRef = protobufItems.SingleOrDefault(p => p.GetMetadataValue("SourceURL") == reference);

                        if (protobufRef == null)
                        {
                            Console.Out.WriteLine($"Warning: Could not find a reference that uses the source url `{reference}`.");
                            continue;
                        }
                    }
                    else
                    {
                        protobufRef = protobufItems.SingleOrDefault(p => p.UnevaluatedInclude == reference);

                        if (protobufRef == null)
                        {
                            Console.Out.WriteLine($"Warning: Could not find a reference for the file `{reference}`.");
                            continue;
                        }
                    }

                    Console.Out.WriteLine($"Removing reference to file {protobufRef.UnevaluatedInclude}");
                    RemoveProtobufReference(protobufRef, removeFile);
                }

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
