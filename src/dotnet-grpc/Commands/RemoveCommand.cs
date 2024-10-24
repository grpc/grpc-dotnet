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
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Dotnet.Cli.Properties;

namespace Grpc.Dotnet.Cli.Commands;

internal sealed class RemoveCommand : CommandBase
{
    public RemoveCommand(IConsole console, string? projectPath, HttpClient httpClient)
        : base(console, projectPath, httpClient) { }

    public static Command Create(HttpClient httpClient)
    {
        var command = new Command(
            name: "remove",
            description: CoreStrings.RemoveCommandDescription);

        var projectOption = CommonOptions.ProjectOption();
        var referencesArgument = new Argument<string[]>
        {
            Name = "references",
            Description = CoreStrings.RemoveCommandArgumentDescription,
            Arity = ArgumentArity.OneOrMore
        };
        
        command.AddOption(projectOption);
        command.AddArgument(referencesArgument);

        command.SetHandler(
            (context) =>
            {
                var project = context.ParseResult.GetValueForOption(projectOption);
                var references = context.ParseResult.GetValueForArgument(referencesArgument);
                try
                {
                    var command = new RemoveCommand(context.Console, project, httpClient);
                    command.Remove(references);

                    context.ExitCode = 0;
                }
                catch (CLIToolException e)
                {
                    context.Console.LogError(e);

                    context.ExitCode = -1;
                }

                return Task.CompletedTask;
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
