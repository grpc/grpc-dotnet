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

using System.CommandLine.IO;
using System.CommandLine.Parsing;
using Grpc.Dotnet.Cli.Commands;
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

        try
        {
            // Arrange
            var testConsole = new TestConsole();
            new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "MultipleReferences")).CopyTo(tempDir);
            var parser = Program.BuildParser(CreateClient());

            // Act
            var result = await parser.InvokeAsync($"list -p {tempDir}", testConsole);

            // Assert
            Assert.AreEqual(0, result, testConsole.Error.ToString()!);

            var project = ProjectCollection.GlobalProjectCollection.LoadedProjects.Single(p => p.DirectoryPath == tempDir);
            project.ReevaluateIfNecessary();

            var output = testConsole.Out.ToString()!;
            var lines = output.Split(Environment.NewLine);

            // First line is the heading and should conatin Protobuf Reference, Service Type, Source URL, Access
            AssertContains(lines[0], "Protobuf Reference");
            AssertContains(lines[0], "Service Type");
            AssertContains(lines[0], "Source URL");
            AssertContains(lines[0], "Access");

            // Second line is the reference to
            //<Protobuf Include="Proto/a.proto">
            //  <SourceUrl>https://contoso.com/greet.proto</SourceUrl>
            //</Protobuf>
            Assert.AreEqual(new string[] { "Proto/a.proto", "Both", "https://contoso.com/greet.proto" }, lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries));

            // Third line is the reference to
            //<Protobuf Include="Proto/b.proto" Access="Internal"/>
            Assert.AreEqual(new string[] { "Proto/b.proto", "Both", "Internal" }, lines[2].Split(' ', StringSplitOptions.RemoveEmptyEntries));
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

        try
        {
            // Arrange
            var testConsole = new TestConsole();
            new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "MultipleReferences")).CopyTo(tempDir);

            // Act
            Directory.SetCurrentDirectory(tempDir);
            var command = new ListCommand(testConsole, null, CreateClient());

            Assert.IsNotNull(command.Project);
            Assert.AreEqual("test.csproj", Path.GetFileName(command.Project.FullPath));

            command.List();

            // Assert
            var output = testConsole.Out.ToString()!;
            var lines = output.Split(Environment.NewLine);

            // First line is the heading and should conatin Protobuf Reference, Service Type, Source URL, Access
            AssertContains(lines[0], "Protobuf Reference");
            AssertContains(lines[0], "Service Type");
            AssertContains(lines[0], "Source URL");
            AssertContains(lines[0], "Access");

            // Second line is the reference to
            //<Protobuf Include="Proto/a.proto">
            //  <SourceUrl>https://contoso.com/greet.proto</SourceUrl>
            //</Protobuf>
            Assert.AreEqual(new string[] { "Proto/a.proto", "Both", "https://contoso.com/greet.proto" }, lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries));

            // Third line is the reference to
            //<Protobuf Include="Proto/b.proto" Access="Internal"/>
            Assert.AreEqual(new string[] { "Proto/b.proto", "Both", "Internal" }, lines[2].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            // Cleanup
            Directory.SetCurrentDirectory(currentDir);
            Directory.Delete(tempDir, true);
        }
    }

    private void AssertContains(string source, string s)
    {
        Assert.True(source.Contains(s), $"Source '{source}' does not contain '{s}'.");
    }
}
