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

namespace Grpc.Dotnet.Cli
{
    public class AddFileCommand
    {
        public static Command CreateCommand()
        {
            var command = new Command(
                "file",
                "Add protobuf file reference(s) to gRPC project."
                ,
                argument: new Argument<string[]>
                {
                    Name = "PROTOBUF-FILE",
                    Description = "The protobuf file reference(s). These can be a path to glob for local protobuf file(s).",
                }
                );

            command.AddOption(
                new Option(
                    new[] { "-p", "--project" },
                    "The project file to operate on. If a file is not specified, the command will search the current directory for one.",
                    new Argument<string> { Name = "PROJECT" }));
            command.AddOption(
                new Option(
                    new[] { "-s", "--services" },
                    "The type of gRPC services that should be generated. Valid values are: Both, Server (Default?), Client, None.",
                    new Argument<string> { Name = "SERVICES" }));
            command.AddOption(
                new Option(
                    new[] { "--access" },
                    "The access modifier to use for the generated C# classes. Valid values are: Public (Default), Internal.",
                    new Argument<string>() { Name = "ACCESS" }));
            command.AddOption(
                new Option(
                    new[] { "-a", "--additional-import-dirs" },
                    "Additional directories to be used when resolving imports for the protobuf files.",
                    new Argument<string> { Name = "ADDITIONAL-IMPORT-DIRS" }));
            command.AddOption(
                new Option(
                    new[] { "-o", "--output-dir" },
                    "The directory to place the generated C# classes for gRPC messages. By default, these files would be placed in the obj directory.",
                    new Argument<string> { Name = "OUTPUT-DIR" }));
            command.AddOption(
                new Option(
                    new[] { "--grpc-output-dir" },
                    "The directory to place the generated C# classes for gRPC services. By default, these files would be placed in the same directory as gRPC messages.",
                    new Argument<string> { Name = "GRPC-OUTPUT-DIR" }));

            command.Handler = CommandHandler.Create<string, string, string, string, string, string, string[]>(AddFile);

            return command;
        }

        public static int AddFile(string project, string services, string access, string outputDir, string grpcOutputDir, string additionalImportDirs, string[] protobufFile)
        {
            System.Console.WriteLine($"{project}");
            System.Console.WriteLine($"{services}");
            System.Console.WriteLine($"{access}");
            System.Console.WriteLine($"{outputDir}");
            System.Console.WriteLine($"{grpcOutputDir}");
            System.Console.WriteLine($"{additionalImportDirs}");
            foreach (var file in protobufFile)
            {
                System.Console.Write($"{file} ");
            }
            return 0;
        }
    }
}
