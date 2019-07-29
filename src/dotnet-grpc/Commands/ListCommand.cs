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
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Dotnet.Cli.Properties;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands
{
    internal class ListCommand : CommandBase
    {
        public ListCommand(IConsole console, FileInfo? projectPath)
            : base(console, projectPath) { }

        public static Command Create()
        {
            var command = new Command(
                name: "list",
                description: CoreStrings.RefreshCommandDescription);

            command.AddOption(CommonOptions.ProjectOption());

            command.Handler = CommandHandler.Create<IConsole, FileInfo>(
                (console, project) =>
                {
                    try
                    {
                        var command = new ListCommand(console, project);
                        command.List();

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

        public  void List()
        {
            // TODO Resx
            Console.Log("Protobuf references:");
            Console.Log("");

            foreach (var reference in Project.GetItems(ProtobufElement))
            {
                if (reference.HasMetadata(SourceUrlElement))
                {
                    Console.Log($"URL reference: {reference.UnevaluatedInclude} from {reference.GetMetadataValue(SourceUrlElement)}");
                }
                else
                {
                    Console.Log($"File reference: {reference.UnevaluatedInclude}");
                }
            }
        }
    }
}
