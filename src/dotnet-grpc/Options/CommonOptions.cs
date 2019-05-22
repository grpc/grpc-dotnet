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

namespace Grpc.Dotnet.Cli.Options
{
    internal static class CommonOptions
    {
        public static Option ProjectOption() =>
            new Option(
                aliases: new[] { "-p", "--project" },
                description: "The project file to operate on. If a file is not specified, the command will search the current directory for one.",
                argument: new Argument<string> { Name = "project" });
        public static Option ServiceOption() =>
            new Option(
                aliases: new[] { "-s", "--services" },
                description: "The type of gRPC services that should be generated. Valid values are: Both, Server (Default), Client, None.",
                argument: new Argument<string> { Name = "services" });
        public static Option AccessOption() =>
            new Option(
                aliases: new[] { "--access" },
                description: "The access modifier to use for the generated C# classes. Valid values are: Public (Default), Internal.",
                argument: new Argument<string> { Name = "access" });
        public static Option AdditionalImportDirsOption() =>
            new Option(
                aliases: new[] { "-a", "--additional-import-dirs" },
                description: "Additional directories to be used when resolving imports for the protobuf files. This is a semicolon ",
                argument: new Argument<string> { Name = "dirs" });
    }
}
