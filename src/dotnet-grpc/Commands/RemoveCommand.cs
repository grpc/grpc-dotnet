﻿#region Copyright notice and license

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
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Dotnet.Cli.Properties;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands
{
    internal class RemoveCommand : CommandBase
    {
        public RemoveCommand(IConsole console, FileInfo? projectPath)
            : base(console, projectPath) { }

        public static Command Create()
        {
            var command = new Command(
                name: "remove",
                description: CoreStrings.RemoveCommandDescription,
                argument: new Argument<string[]>
                {
                    Name = "references",
                    Description = CoreStrings.RemoveCommandArgumentDescription,
                    Arity = ArgumentArity.OneOrMore
                });

            command.AddOption(CommonOptions.ProjectOption());

            command.Handler = CommandHandler.Create<IConsole, FileInfo, string[]>(
                (console, project, references) =>
                {
                    try
                    {
                        var command = new RemoveCommand(console, project);
                        command.Remove(references);

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

        public void Remove(string[] references)
        {
            var items = ResolveReferences(references);

            foreach (var item in items)
            {
                Console.Log(CoreStrings.LogRemoveReference, item.UnevaluatedInclude);
                Project.RemoveItem(item);
            }

            Project.Save();
        }
    }
}
