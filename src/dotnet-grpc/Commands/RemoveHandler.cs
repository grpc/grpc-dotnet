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
using Grpc.Dotnet.Cli.Extensions;
using Grpc.Dotnet.Cli.Options;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands
{
    internal static class RemoveHandler
    {
        public static Command RemoveCommand()
        {
            var command = new Command(
                name: "remove",
                description: "Remove protobuf files(s).",
                argument: new Argument<string[]>
                {
                    Name = "references",
                    Description = "The URL(s) or file path(s) of the protobuf references to remove.",
                    Arity = ArgumentArity.OneOrMore
                });

            command.AddOption(CommonOptions.ProjectOption());
            command.AddOption(new Option(
                aliases: new[] { "--remove-file" },
                description: "Also delete the protobuf file from disk.",
                argument: Argument.None));

            command.Handler = CommandHandler.Create<FileInfo, bool, string[]>(Remove);

            return command;
        }

        public static void Remove(FileInfo? project, bool removeFile, string[] references)
        {
            var msBuildProject = ProjectExtensions.ResolveProject(project);
            var protobufItems = msBuildProject.GetItems("Protobuf");
            var refsToRefresh = new List<ProjectItem>();
            references = msBuildProject.ExpandReferences(references);

            foreach (var reference in references)
            {
                ProjectItem protobufRef;

                if (ProjectExtensions.IsUrl(reference))
                {
                    protobufRef = protobufItems.SingleOrDefault(p => p.GetMetadataValue("SourceURL") == reference);

                    if (protobufRef == null)
                    {
                        Console.WriteLine($"Could not find a reference that uses the source url `{reference}`.");
                        continue;
                    }

                    msBuildProject.RemoveProtobufReference(protobufRef, removeFile);
                }
                else
                {
                    protobufRef = protobufItems.SingleOrDefault(p => p.UnevaluatedInclude == reference);

                    if (protobufRef == null)
                    {
                        Console.WriteLine($"Could not find a reference for the file `{reference}`.");
                        continue;
                    }
                }

                msBuildProject.RemoveProtobufReference(protobufRef, removeFile);
            }

            msBuildProject.Save();
        }
    }
}
