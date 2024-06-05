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
using Grpc.Dotnet.Cli.Options;
using Microsoft.Build.Evaluation;
using NUnit.Framework;

namespace Grpc.Dotnet.Cli.Tests;

[TestFixture]
public class AddFileCommandTests : TestBase
{
    [Test]
    [NonParallelizable]
    public async Task Commandline_AddFileCommand_AddsPackagesAndReferences()
    {
        // Arrange
        var currentDir = Directory.GetCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var testConsole = new TestConsole();
        new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "EmptyProject")).CopyTo(tempDir);

        var parser = Program.BuildParser(CreateClient());

        // Act
        var result = await parser.InvokeAsync($"add-file -p {tempDir} -s Server --access Internal -i ImportDir {Path.Combine("Proto", "*.proto")}", testConsole);

        // Assert
        Assert.AreEqual(0, result, testConsole.Error.ToString()!);

        var project = ProjectCollection.GlobalProjectCollection.LoadedProjects.Single(p => p.DirectoryPath == tempDir);
        project.ReevaluateIfNecessary();

        var packageRefs = project.GetItems(CommandBase.PackageReferenceElement);
        Assert.AreEqual(1, packageRefs.Count);
        Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.AspNetCore" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));

        var protoRefs = project.GetItems(CommandBase.ProtobufElement);
        Assert.AreEqual(2, protoRefs.Count);
        Assert.NotNull(protoRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Proto\\a.proto"));
        Assert.NotNull(protoRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Proto\\b.proto"));
        foreach (var protoRef in protoRefs)
        {
            Assert.AreEqual("Server", protoRef.GetMetadataValue(CommandBase.GrpcServicesElement));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue(CommandBase.AdditionalImportDirsElement));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue(CommandBase.AccessElement));
        }

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Test]
    [NonParallelizable]
    public async Task AddFileCommand_AddsPackagesAndReferences()
    {
        // Arrange
        var currentDir = Directory.GetCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "EmptyProject")).CopyTo(tempDir);

        // Act
        Directory.SetCurrentDirectory(tempDir);
        var command = new AddFileCommand(new TestConsole(), projectPath: null, CreateClient());
        await command.AddFileAsync(Services.Server, Access.Internal, "ImportDir", new[] { Path.Combine("Proto", "*.proto") });
        command.Project.ReevaluateIfNecessary();

        // Assert
        var packageRefs = command.Project.GetItems(CommandBase.PackageReferenceElement);
        Assert.AreEqual(1, packageRefs.Count);
        Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.AspNetCore" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));


        var protoRefs = command.Project.GetItems(CommandBase.ProtobufElement);
        Assert.AreEqual(2, protoRefs.Count);
        Assert.NotNull(protoRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Proto\\a.proto"));
        Assert.NotNull(protoRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Proto\\b.proto"));
        foreach (var protoRef in protoRefs)
        {
            Assert.AreEqual("Server", protoRef.GetMetadataValue(CommandBase.GrpcServicesElement));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue(CommandBase.AdditionalImportDirsElement));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue(CommandBase.AccessElement));
        }

        // Cleanup
        Directory.SetCurrentDirectory(currentDir);
        Directory.Delete(tempDir, true);
    }
}
