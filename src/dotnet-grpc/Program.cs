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
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using Grpc.Dotnet.Cli.Commands;
using Microsoft.Build.Locator;

namespace Grpc.Dotnet.Cli
{
    public class Program
    {
        public static Task<int> Main(string[] args)
        {
            MSBuildLocator.RegisterDefaults();

            var parser = BuildParser(new HttpClient());
            var result = parser.Parse(args);

            return result.InvokeAsync(new SystemConsole());
        }

        internal static Parser BuildParser(HttpClient client)
        {
            var root = new RootCommand();
            root.AddCommand(AddFileCommand.Create(client));
            root.AddCommand(AddUrlCommand.Create(client));
            root.AddCommand(RefreshCommand.Create(client));
            root.AddCommand(RemoveCommand.Create(client));
            root.AddCommand(ListCommand.Create(client));

            var parser = new CommandLineBuilder(root)
                .UseDefaults()
                .Build();
            return parser;
        }
    }
}
