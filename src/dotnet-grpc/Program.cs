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

using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Commands;
using Microsoft.Build.Locator;

namespace Grpc.Dotnet.Cli
{
    public class Program
    {
        public static Task<int> Main(string[] args)
        {
            MSBuildLocator.RegisterDefaults();

            var parser = new CommandLineBuilder()
                .AddCommand(AddFileCommand.Create())
                .AddCommand(AddUrlCommand.Create())
                .AddCommand(RefreshCommand.Create())
                .AddCommand(RemoveCommand.Create())
                .AddCommand(ListCommand.Create())
                .UseDefaults()
                .Build();

            var result = parser.Parse(args);

            return result.InvokeAsync(new SystemConsole());
        }
    }
}
