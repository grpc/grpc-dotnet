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
using System.CommandLine.Parsing;
using Grpc.Dotnet.Cli.Commands;
using Grpc.Dotnet.Cli.Internal;
using Microsoft.Build.Evaluation;
using NUnit.Framework;

namespace Grpc.Dotnet.Cli.Tests;

[TestFixture]
public class ListCommandTests : TestBase
{
    [Test]
    public async Task Commandline_List_ListsReferences()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outWriter = new StringWriter();
        var errorWriter = new StringWriter();

        try
        {
            // Arrange
            new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "MultipleReferences")).CopyTo(tempDir);
            var rootCommand = Program.BuildRootCommand(CreateClient());

            // Act
            var result = rootCommand.Parse($"list -p {tempDir}");
            var errorCode = await result.InvokeAsync(configuration: new InvocationConfiguration { Output = outWriter, Error = errorWriter });

            // Assert
            Assert.AreEqual(0, errorCode, errorWriter.ToString());

            var project = ProjectCollection.GlobalProjectCollection.LoadedProjects.Single(p => p.DirectoryPath == tempDir);
            project.ReevaluateIfNecessary();

            var output = outWriter.ToString();

            //<Protobuf Include="Proto/a.proto">
            //  <SourceUrl>https://contoso.com/greet.proto</SourceUrl>
            //</Protobuf>
            //<Protobuf Include="Proto/b.proto" Access="Internal"/>
            var expected = """
                Protobuf Reference: Proto/a.proto
                Service Type: Both
                Source URL: https://contoso.com/greet.proto

                Protobuf Reference: Proto/b.proto
                Service Type: Both
                Access: Internal
                """;
            Assert.AreEqual(expected.Trim(), output.Trim());
        }
        finally
        {
            // Cleanup
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void List_ListsReferences()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outWriter = new StringWriter();
        var errorWriter = new StringWriter();

        try
        {
            // Arrange
            new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "MultipleReferences")).CopyTo(tempDir);

            // Act
            Directory.SetCurrentDirectory(tempDir);
            var command = new ListCommand(new ConsoleService(outWriter, errorWriter), null, CreateClient());

            Assert.IsNotNull(command.Project);
            Assert.AreEqual("test.csproj", Path.GetFileName(command.Project.FullPath));

            command.List();

            // Assert
            var output = outWriter.ToString();

            //<Protobuf Include="Proto/a.proto">
            //  <SourceUrl>https://contoso.com/greet.proto</SourceUrl>
            //</Protobuf>
            //<Protobuf Include="Proto/b.proto" Access="Internal"/>
            var expected = """
                Protobuf Reference: Proto/a.proto
                Service Type: Both
                Source URL: https://contoso.com/greet.proto

                Protobuf Reference: Proto/b.proto
                Service Type: Both
                Access: Internal
                """;
            Assert.AreEqual(expected.Trim(), output.Trim());
        }
        finally
        {
            // Cleanup
            Directory.SetCurrentDirectory(currentDir);
            Directory.Delete(tempDir, true);
        }
    }
}
