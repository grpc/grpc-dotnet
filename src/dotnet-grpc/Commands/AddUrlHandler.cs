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
using Grpc.Dotnet.Cli.Options;

namespace Grpc.Dotnet.Cli.Commands
{
    internal static class AddUrlHandler
    {
        public static Command AddFileCommand()
        {
            var command = new Command(
                name: "add-url",
                description: "Add a protobuf url reference to the gRPC project.",
                argument: new Argument<string>
                {
                    Name = "url",
                    Description = "The URL to a remote protobuf file.",
                });

            command.AddOption(CommonOptions.ProjectOption());
            command.AddOption(CommonOptions.ServiceOption());
            command.AddOption(CommonOptions.AdditionalImportDirsOption());
            command.AddOption(CommonOptions.AccessOption());

            command.Handler = CommandHandler.Create<string, string, string, string, string>(AddUrl);

            return command;
        }

        public static int AddUrl(string project, string services, string additionalImportDirs, string access, string url)
        {
            System.Console.WriteLine($"{project}");
            System.Console.WriteLine($"{services}");
            System.Console.WriteLine($"{additionalImportDirs}");
            System.Console.WriteLine($"{access}");
            System.Console.Write($"{url} ");

            return 0;
        }
    }
}
