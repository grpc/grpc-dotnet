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
                description: "Also delete the protobuf file from disk."));

            command.Handler = CommandHandler.Create<FileInfo, bool, string[]>(Remove);

            return command;
        }

        public static int Remove(FileInfo? project, bool removeFile, string[] references)
        {
            if (project == null)
            {
                project = ProjectExtensions.ResolveProjectPath();

                if (project == null)
                {
                    return -1;
                }
            }

            var msBuildProject = new Project(project.FullName);
            var protobufItems = msBuildProject.GetItems("Protobuf");
            var refsToRefresh = new List<ProjectItem>();

            foreach (var reference in references)
            {
                if (Uri.TryCreate(reference, UriKind.Absolute, out var _) && reference.StartsWith("http"))
                {
                    var protobufRef = protobufItems.SingleOrDefault(p => p.GetMetadataValue("SourceURL") == reference);

                    if (protobufRef == null)
                    {
                        Console.WriteLine($"Could not find a reference that uses the source url `{reference}`.");
                        continue;
                    }

                    msBuildProject.RemoveItem(protobufRef);

                    if (removeFile)
                    {
                        File.Delete(Path.IsPathRooted(protobufRef.UnevaluatedInclude) ? protobufRef.UnevaluatedInclude : Path.Combine(project.DirectoryName, protobufRef.UnevaluatedInclude));
                    }
                }
                else
                {
                    var protobufRef = protobufItems.SingleOrDefault(p => p.UnevaluatedInclude == reference);

                    if (protobufRef == null)
                    {
                        Console.WriteLine($"Could not find a reference for the file `{reference}`.");
                        continue;
                    }

                    msBuildProject.RemoveItem(protobufRef);

                    if (removeFile)
                    {
                        File.Delete(Path.IsPathRooted(protobufRef.UnevaluatedInclude) ? protobufRef.UnevaluatedInclude : Path.Combine(project.DirectoryName, protobufRef.UnevaluatedInclude));
                    }
                }
            }

            msBuildProject.Save();

            return 0;
        }
    }
}
