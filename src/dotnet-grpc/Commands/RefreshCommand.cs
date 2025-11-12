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

internal sealed class RefreshCommand : CommandBase
{
    public RefreshCommand(ConsoleService console, string? projectPath, HttpClient httpClient)
        : base(console, projectPath, httpClient) { }

    // Internal for testing
    public RefreshCommand(ConsoleService console, HttpClient client)
        : base(console, client) { }

    public static Command Create(HttpClient httpClient)
    {
        var command = new Command(
            name: "refresh",
            description: CoreStrings.RefreshCommandDescription);

        var projectOption = CommonOptions.ProjectOption();
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = CoreStrings.DryRunOptionDescription
        };
        var referencesArgument = new Argument<string[]>("references")
        {
            Description = CoreStrings.RefreshCommandArgumentDescription,
            Arity = ArgumentArity.ZeroOrMore
        };

        command.Add(projectOption);
        command.Add(dryRunOption);
        command.Add(referencesArgument);

        command.SetAction(
            async (context) =>
            {
                var project = context.GetValue(projectOption);
                var dryRun = context.GetValue(dryRunOption);
                var references = context.GetValue(referencesArgument) ?? [];

                var console = new ConsoleService(context.InvocationConfiguration.Output, context.InvocationConfiguration.Error);
                try
                {
                    var command = new RefreshCommand(console, project, httpClient);
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
        var refsToRefresh = references == null || references.Length == 0 ? Project.GetItems(ProtobufElement).Where(p => p.HasMetadata(SourceUrlElement)) : ResolveReferences(references);

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
