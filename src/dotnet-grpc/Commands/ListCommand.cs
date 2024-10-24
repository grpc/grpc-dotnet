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
using System.CommandLine.Rendering;
using System.CommandLine.Rendering.Views;
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Dotnet.Cli.Properties;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands;

internal sealed class ListCommand : CommandBase
{
    public ListCommand(IConsole console, string? projectPath, HttpClient httpClient)
        : base(console, projectPath, httpClient) { }

    public static Command Create(HttpClient httpClient)
    {
        var command = new Command(
            name: "list",
            description: CoreStrings.ListCommandDescription);
        var projectOption = CommonOptions.ProjectOption();

        command.AddOption(projectOption);

        command.SetHandler(
            (context) =>
            {
                var project = context.ParseResult.GetValueForOption(projectOption);
                try
                {
                    var command = new ListCommand(context.Console, project, httpClient);
                    command.List();

                    context.ExitCode = 0;
                }
                catch (CLIToolException e)
                {
                    context.Console.LogError(e);

                    context.ExitCode = -1;
                }
            });

        return command;
    }

    public void List()
    {
        var consoleRenderer = new ConsoleRenderer(Console);
        var protobufElements = Project.GetItems(ProtobufElement).ToList();
        if (protobufElements.Count == 0)
        {
            Console.Log(CoreStrings.LogNoReferences);
            return;
        }

        var table = new TableView<ProjectItem> { Items = protobufElements };

        // Required columns (always displayed)
        table.AddColumn(r => r.UnevaluatedInclude, CoreStrings.TableColumnProtobufReference);
        table.AddColumn(r =>
        {
            var serviceType = r.GetMetadataValue(GrpcServicesElement);
            return string.IsNullOrEmpty(serviceType) ? "Both" : serviceType;
        }, CoreStrings.TableColumnServiceType);

        // Optional columns (only displayed if an element is not default)
        if (protobufElements.Any(r => !string.IsNullOrEmpty(r.GetMetadataValue(SourceUrlElement))))
        {
            table.AddColumn(r => r.GetMetadataValue(SourceUrlElement), CoreStrings.TableColumnSourceUrl);
        }

        // The default value is Public set by Grpc.Tools so skip this column if everything is default
        if (protobufElements.Any(r => !string.Equals(r.GetMetadataValue(AccessElement), Access.Public.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            table.AddColumn(r => r.GetMetadataValue(AccessElement), CoreStrings.TableColumnAccess);
        }

        if (protobufElements.Any(r => !string.IsNullOrEmpty(r.GetMetadataValue(AdditionalImportDirsElement))))
        {
            table.AddColumn(r => r.GetMetadataValue(AdditionalImportDirsElement), CoreStrings.TableColumnAdditionalImports);
        }

        var screen = new ScreenView(consoleRenderer, Console) { Child = table };
        Region region;
        try
        {
            // Some environments incorrectly report zero width when there is no console
            var width = System.Console.WindowWidth;
            if (width == 0)
            {
                width = int.MaxValue;
            }

            var height = System.Console.WindowHeight;
            if (height == 0)
            {
                height = int.MaxValue;
            }

            region = new Region(0, 0, width, height);
        }
        catch (IOException)
        {
            // System.Console.WindowWidth can throw an IOException when runnning without a console attached
            region = new Region(0, 0, int.MaxValue, int.MaxValue);
        }
        screen.Child?.Render(consoleRenderer, region);
    }
}
