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
using Grpc.Dotnet.Cli.Commands;
using Microsoft.Build.Locator;

namespace Grpc.Dotnet.Cli;

public class Program
{
    public static Task<int> Main(string[] args)
    {
        MSBuildLocator.RegisterDefaults();

        var rootCommand = BuildRootCommand(new HttpClient());
        var result = rootCommand.Parse(args);

        return result.InvokeAsync();
    }

    internal static RootCommand BuildRootCommand(HttpClient client)
    {
        var root = new RootCommand
        {
            AddFileCommand.Create(client),
            AddUrlCommand.Create(client),
            RefreshCommand.Create(client),
            RemoveCommand.Create(client),
            ListCommand.Create(client)
        };

        return root;
    }
}
