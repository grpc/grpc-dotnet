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
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands;

internal sealed class ListCommand : CommandBase
{
    public ListCommand(ConsoleService console, string? projectPath, HttpClient httpClient)
        : base(console, projectPath, httpClient) { }

    public static Command Create(HttpClient httpClient)
    {
        var command = new Command(
            name: "list",
            description: CoreStrings.ListCommandDescription);
        var projectOption = CommonOptions.ProjectOption();

        command.Add(projectOption);

        command.SetAction(
            (context) =>
            {
                var project = context.GetValue(projectOption);

                var console = new ConsoleService(context.InvocationConfiguration.Output, context.InvocationConfiguration.Error);
                try
                {
                    var command = new ListCommand(console, project, httpClient);
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

    private class ListProtobufElement
    {
        public required string ProtobufReference { get; init; }
        public required string ServiceType { get; init; }

        public string? SourceUrl { get; init; }
        public string? Access { get; init; }
        public string? AdditionalImportDirs { get; init; }
    }

    public void List()
    {
        var protobufElements = Project.GetItems(ProtobufElement).ToList();
        if (protobufElements.Count == 0)
        {
            Console.Log(CoreStrings.LogNoReferences);
            return;
        }

        var typedProtobufElements = protobufElements.Select(e => new ListProtobufElement
        {
            ProtobufReference = e.UnevaluatedInclude,
            ServiceType = e.GetMetadataValue(GrpcServicesElement) is { Length: > 0 } serviceType ? serviceType : "Both",
            SourceUrl = e.GetMetadataValue(SourceUrlElement),
            Access = e.GetMetadataValue(AccessElement),
            AdditionalImportDirs = e.GetMetadataValue(AdditionalImportDirsElement)
        }).ToList();

        foreach (var element in typedProtobufElements)
        {
            // Required columns (always displayed)
            Console.Log("{0}: {1}", CoreStrings.TableColumnProtobufReference, element.ProtobufReference);
            Console.Log("{0}: {1}", CoreStrings.TableColumnServiceType, element.ServiceType);

            // Optional columns (only displayed if an element is not default)
            if (!string.IsNullOrEmpty(element.SourceUrl))
            {
                Console.Log("{0}: {1}", CoreStrings.TableColumnSourceUrl, element.SourceUrl);
            }
            // The default value is Public set by Grpc.Tools so skip this column if everything is default
            if (!string.IsNullOrEmpty(element.Access) && !string.Equals(element.Access, Access.Public.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                Console.Log("{0}: {1}", CoreStrings.TableColumnAccess, element.Access);
            }
            if (!string.IsNullOrEmpty(element.AdditionalImportDirs))
            {
                Console.Log("{0}: {1}", CoreStrings.TableColumnAdditionalImports, element.AdditionalImportDirs);
            }

            Console.Log(string.Empty);
        }
    }
}
