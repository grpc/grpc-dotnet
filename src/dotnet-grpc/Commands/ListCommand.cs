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

using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.CommandLine.Rendering.Views;
using System.Globalization;
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
                description: CoreStrings.ListCommandDescription);
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
            var consoleRenderer = new ConsoleRenderer(Console);
            var protobufElements = Project.GetItems(ProtobufElement).ToList();
            var table = new TableView<ProjectItem> { Items = protobufElements};

            // Required columns (always displayed)
            table.AddColumn(r => r.UnevaluatedInclude, "Protobuf Reference");
            table.AddColumn(r =>
            {
                var serviceType = r.GetMetadataValue(GrpcServicesElement);
                return string.IsNullOrEmpty(serviceType) ? "Both" : serviceType;
            }, "Service Type");

            // Optional columns (only displayed if an element is not default)
            if (protobufElements.Any(r => !string.IsNullOrEmpty(r.GetMetadataValue(SourceUrlElement))))
            {
                table.AddColumn(r => r.GetMetadataValue(SourceUrlElement), "Source URL");
            }

            // The default value is Public set by Grpc.Tools so skip this column if everything is default
            if (protobufElements.Any(r => !string.Equals(r.GetMetadataValue(AccessElement), Access.Public.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                table.AddColumn(r => r.GetMetadataValue(AccessElement), "Access");
            }

            if (protobufElements.Any(r => !string.IsNullOrEmpty(r.GetMetadataValue(AdditionalImportDirsElement))))
            {
                table.AddColumn(r => r.GetMetadataValue(AdditionalImportDirsElement), "Additional Imports");
            }

            var screen = new ScreenView(consoleRenderer, Console) { Child = table };
            screen.Render(new Region(0, 0, System.Console.WindowWidth, System.Console.WindowWidth));
        }
    }
}
