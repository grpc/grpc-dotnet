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
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Extensions;
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

            command.Handler = CommandHandler.Create<FileInfo, Services, Access, string, string[]>(AddFile);

            return command;
        }

        public static async Task<int> AddFile(FileInfo project, Services services, Access access, string additionalImportDirs, string[] files)
        {
            using (var projectCollection = new ProjectCollection())
            {
                var msBuildProject = Project.FromFile(project.FullName, new ProjectOptions { ProjectCollection = projectCollection });

                var exitCode = await msBuildProject.EnsureGrpcPackagesAsync();
                if (exitCode != 0)
                {
                    return exitCode;
                }

                foreach (var file in files)
                {
                    msBuildProject.AddProtobufReference(services, additionalImportDirs, access, file, string.Empty);
                }

                msBuildProject.Save();
            }

            return 0;
        }
    }
}
