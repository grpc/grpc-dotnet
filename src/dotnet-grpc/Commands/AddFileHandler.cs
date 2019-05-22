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
using System.Linq;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Options;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands
{
    internal static class AddFileHandler
    {
        public static Command AddFileCommand()
        {
            var command = new Command(
                name: "add-file",
                description: "Add protobuf file reference(s) to the gRPC project.",
                argument: new Argument<string[]>
                {
                    Name = "files",
                    Description = "The protobuf file reference(s). These can be a path to glob for local protobuf file(s).",
                });

            command.AddOption(CommonOptions.ProjectOption());
            command.AddOption(CommonOptions.ServiceOption());
            command.AddOption(CommonOptions.AdditionalImportDirsOption());
            command.AddOption(CommonOptions.AccessOption());

            command.Handler = CommandHandler.Create<string, string, string, string, string[]>(AddFile);

            return command;
        }

        public static async Task<int> AddFile(string project, string services, string additionalImportDirs, string access, string[] files)
        {
            Console.WriteLine($"Project: {project}");
            Console.WriteLine($"Services: {services}");
            Console.WriteLine($"Access: {access}");
            Console.WriteLine($"Additional Import Dirs: {additionalImportDirs}");

            foreach (var file in files)
            {
                Console.Write($"{file} ");
            }
            Console.WriteLine();

            using (var projectCollection = new ProjectCollection())
            {
                var msBuildProject = Project.FromFile(project, new ProjectOptions { ProjectCollection = projectCollection });

                var exitCode = await ProjectUtilities.EnsureGrpcPackagesAsync(msBuildProject);
                if (exitCode != 0)
                {
                    return exitCode;
                }

                foreach (var file in files)
                {
                    if (!msBuildProject.Items.Any(i => i.ItemType == "Protobuf" && i.UnevaluatedInclude == file))
                    {
                        var metadata = new List<KeyValuePair<string, string>>();

                        if (services != "Both")
                        {
                            metadata.Add(new KeyValuePair<string, string>("GrpcServices", services));
                        }

                        if (access != "Public")
                        {
                            metadata.Add(new KeyValuePair<string, string>("Access", access));
                        }

                        if (!string.IsNullOrEmpty(additionalImportDirs))
                        {
                            metadata.Add(new KeyValuePair<string, string>("AdditionalImportDirs", additionalImportDirs));
                        }

                        msBuildProject.AddItem("Protobuf", file, metadata);
                    }
                }

                msBuildProject.Save();

                return 0;
            }
        }
    }
}
