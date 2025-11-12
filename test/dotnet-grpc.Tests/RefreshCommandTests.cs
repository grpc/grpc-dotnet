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
using System.Globalization;
using Grpc.Dotnet.Cli.Commands;
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Properties;
using Microsoft.Build.Evaluation;
using NUnit.Framework;

namespace Grpc.Dotnet.Cli.Tests;

[TestFixture]
public class RefreshCommandTests : TestBase
{
    [TestCase(true)]
    [TestCase(false)]
    [NonParallelizable]
    public async Task Commandline_Refresh_RefreshesReferences(bool dryRun)
    {
        // Arrange
        var currentDir = Directory.GetCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outWriter = new StringWriter();
        var errorWriter = new StringWriter();
        new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "ProjectWithReference")).CopyTo(tempDir);

        var rootCommand = Program.BuildRootCommand(CreateClient());

        // Act
        var result = rootCommand.Parse($"refresh -p {tempDir} --dry-run {dryRun}");
        var errorCode = await result.InvokeAsync(configuration: new InvocationConfiguration { Output = outWriter, Error = errorWriter });

        // Assert
        Assert.AreEqual(0, errorCode, errorWriter.ToString());

        var project = ProjectCollection.GlobalProjectCollection.LoadedProjects.Single(p => p.DirectoryPath == tempDir);
        project.ReevaluateIfNecessary();

        Assert.AreEqual(string.Format(CultureInfo.InvariantCulture, CoreStrings.LogDownload, "Proto/a.proto", SourceUrl), outWriter.ToString().TrimEnd());
        Assert.AreEqual(dryRun, string.IsNullOrEmpty(File.ReadAllText(Path.Combine(project.DirectoryPath, "Proto", "a.proto"))));

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [TestCase(true)]
    [TestCase(false)]
    [NonParallelizable]
    public async Task Refresh_RefreshesReferences(bool dryRun)
    {
        // Arrange
        var currentDir = Directory.GetCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outWriter = new StringWriter();
        new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "ProjectWithReference")).CopyTo(tempDir);

        // Act
        Directory.SetCurrentDirectory(tempDir);
        var command = new RefreshCommand(new ConsoleService(outWriter, TextWriter.Null), CreateClient());
        await command.RefreshAsync(dryRun, Array.Empty<string>());

        // Assert
        Assert.AreEqual(string.Format(CultureInfo.InvariantCulture, CoreStrings.LogDownload, "Proto/a.proto", SourceUrl), outWriter.ToString().TrimEnd());
        Assert.AreEqual(dryRun, string.IsNullOrEmpty(File.ReadAllText(Path.Combine(command.Project.DirectoryPath, "Proto", "a.proto"))));

        // Cleanup
        Directory.SetCurrentDirectory(currentDir);
        Directory.Delete(tempDir, true);
    }
}
