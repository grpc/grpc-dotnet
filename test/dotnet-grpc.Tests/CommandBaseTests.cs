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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Commands;
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;
using NUnit.Framework;

namespace Grpc.Dotnet.Cli.Tests
{
    [TestFixture]
    public class BindMethodFinderTests
    {
        private static readonly string ProtoUrl = "https://raw.githubusercontent.com/grpc/grpc-dotnet/edf4b478e5d35f19e69943eb807a99709fc8de3b/examples/Proto/greet.proto";

        [OneTimeSetUp]
        public void Initialize()
        {
            MSBuildLocator.RegisterDefaults();
        }

        [Test]
        public void EnsureNugetPackages_AddsRequiredPackages()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();
            commandBase.Console = new TestConsole();

            // Act
            commandBase.EnsureNugetPackages();
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var packageRefs = commandBase.Project.GetItems("PackageReference");
            Assert.AreEqual(3, packageRefs.Count);
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Google.Protobuf" && !r.HasMetadata("PrivateAssets")));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.AspNetCore.Server" && !r.HasMetadata("PrivateAssets")));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.Tools" && r.HasMetadata("PrivateAssets")));
        }

        [Test]
        public void EnsureNugetPackages_DoesNotOverwriteExistingPackageReferences()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();
            commandBase.Console = new TestConsole();
            commandBase.Project.AddItem("PackageReference", "Grpc.Tools");

            // Act
            commandBase.EnsureNugetPackages();
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var packageRefs = commandBase.Project.GetItems("PackageReference");
            Assert.AreEqual(3, packageRefs.Count);
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Google.Protobuf" && !r.HasMetadata("PrivateAssets")));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.AspNetCore.Server" && !r.HasMetadata("PrivateAssets")));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.Tools" && !r.HasMetadata("PrivateAssets")));
        }

        [Test]
        public void AddProtobufReference_ThrowsIfFileNotFound()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();
            commandBase.Console = new TestConsole();

            // Act, Assert
            Assert.Throws<CLIToolException>(() => commandBase.AddProtobufReference(Services.Both, string.Empty, Access.Public, "NonExistentFile", string.Empty));
        }

        [Test]
        public void AddProtobufReference_AddsRelativeReference()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = Project.FromFile(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "test.csproj"), new ProjectOptions { ProjectCollection = new ProjectCollection() });
            commandBase.Console = new TestConsole();
            var referencePath = Path.Combine("Proto", "a.proto");

            // Act
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, referencePath, "http://contoso.com/proto.proto");
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems("Protobuf");
            Assert.AreEqual(1, protoRefs.Count);
            var protoRef = protoRefs.Single();
            Assert.AreEqual(referencePath, protoRef.UnevaluatedInclude);
            Assert.AreEqual("Server", protoRef.GetMetadataValue("GrpcServices"));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue("AdditionalImportDirs"));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue("Access"));
            Assert.AreEqual("http://contoso.com/proto.proto", protoRef.GetMetadataValue("SourceUrl"));
        }

        [Test]
        public void AddProtobufReference_AddsAbsoluteReference()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();
            commandBase.Console = new TestConsole();
            var referencePath = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "a.proto");

            // Act
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, referencePath, "http://contoso.com/proto.proto");
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems("Protobuf");
            Assert.AreEqual(1, protoRefs.Count);
            var protoRef = protoRefs.Single();
            Assert.AreEqual(referencePath, protoRef.UnevaluatedInclude);
            Assert.AreEqual("Server", protoRef.GetMetadataValue("GrpcServices"));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue("AdditionalImportDirs"));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue("Access"));
            Assert.AreEqual("http://contoso.com/proto.proto", protoRef.GetMetadataValue("SourceUrl"));
        }

        [Test]
        public void AddProtobufReference_DoesNotOverwriteReference()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();
            commandBase.Console = new TestConsole();
            var referencePath = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "a.proto");

            // Act
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, referencePath, "http://contoso.com/proto.proto");
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems("Protobuf");
            Assert.AreEqual(1, protoRefs.Count);
            var protoRef = protoRefs.Single();
            Assert.AreEqual(referencePath, protoRef.UnevaluatedInclude);
            Assert.AreEqual("Server", protoRef.GetMetadataValue("GrpcServices"));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue("AdditionalImportDirs"));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue("Access"));
            Assert.AreEqual("http://contoso.com/proto.proto", protoRef.GetMetadataValue("SourceUrl"));
        }

        [Test]
        public void ResolveProject_ThrowsIfProjectFileDoesNotExist()
        {
            // Arrange
            var commandBase = new CommandBase();

            // Act, Assert
            Assert.Throws<CLIToolException>(() => commandBase.ResolveProject(new FileInfo("NonExistent")));
        }

        [Test]
        [NonParallelizable]
        public void ResolveProject_SucceedIfOnlyOneProject()
        {
            // Arrange
            var commandBase = new CommandBase();
            var currentDirectory = Directory.GetCurrentDirectory();
            var testAssetsDirectory = Path.Combine(currentDirectory, "TestAssets");
            Directory.SetCurrentDirectory(testAssetsDirectory);

            // Act
            var project = commandBase.ResolveProject(null);

            // Assert
            Assert.AreEqual(Path.Combine(testAssetsDirectory, "test.csproj"), project.FullPath);
            Directory.SetCurrentDirectory(currentDirectory);
        }

        [Test]
        [NonParallelizable]
        public void ResolveProject_ThrowsIfMoreThanOneProjectFound()
        {
            // Arrange
            var commandBase = new CommandBase();
            var currentDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Path.Combine(currentDirectory, "TestAssets", "DuplicateProjects"));

            // Act, Assert
            Assert.Throws<CLIToolException>(() => commandBase.ResolveProject(null));
            Directory.SetCurrentDirectory(currentDirectory);
        }

        [Test]
        [NonParallelizable]
        public void ResolveProject_ThrowsIfZeroProjectFound()
        {
            // Arrange
            var commandBase = new CommandBase();
            var currentDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Path.Combine(currentDirectory, "TestAssets", "Proto"));

            // Act, Assert
            Assert.Throws<CLIToolException>(() => commandBase.ResolveProject(null));
            Directory.SetCurrentDirectory(currentDirectory);
        }

        [Test]
        public void GlobReferences_ExpandsRelativeReferences()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = Project.FromFile(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "test.csproj"), new ProjectOptions { ProjectCollection = new ProjectCollection() });

            // Act
            var references = commandBase.GlobReferences(new[] { Path.Combine("Proto", "*.proto") });

            // Assert
            Assert.Contains(Path.Combine("Proto", "a.proto"), references);
            Assert.Contains(Path.Combine("Proto", "b.proto"), references);
        }

        [Test]
        public void GlobReferences_ExpandsAbsoluteReferences()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();

            // Act
            var references = commandBase.GlobReferences(new[] { Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "*.proto") });

            // Assert
            Assert.Contains(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "a.proto"), references);
            Assert.Contains(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "b.proto"), references);
        }

        [Test]
        public async Task DownloadFileAsync_DownloadsRemoteFile()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();
            commandBase.Console = new TestConsole();
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "c.proto");

            // Act
            await commandBase.DownloadFileAsync(ProtoUrl, tempProtoFile);

            // Assert
            Assert.IsNotEmpty(File.ReadAllText(tempProtoFile));
            File.Delete(tempProtoFile);
        }

        [Test]
        public async Task DownloadFileAsync_DownloadsRemoteFile_OverwritesIfContentDoesNotMatch()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();
            commandBase.Console = new TestConsole();
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "c.proto");

            // Act
            File.WriteAllText(tempProtoFile, "NonEquivalent Content");
            await commandBase.DownloadFileAsync(ProtoUrl, tempProtoFile);

            // Assert
            Assert.AreNotEqual("NonEquivalent Content", File.ReadAllText(tempProtoFile));
            File.Delete(tempProtoFile);
        }

        [Test]
        public async Task DownloadFileAsync_DownloadsRemoteFile_SkipIfContentMatches()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();
            commandBase.Console = new TestConsole();
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "c.proto");

            // Act
            await commandBase.DownloadFileAsync(ProtoUrl, tempProtoFile);
            var lastWriteTime = File.GetLastWriteTime(tempProtoFile);
            await commandBase.DownloadFileAsync(ProtoUrl, tempProtoFile);

            // Assert
            Assert.AreEqual(lastWriteTime, File.GetLastWriteTime(tempProtoFile));
            File.Delete(tempProtoFile);
        }

        [Test]
        public async Task DownloadFileAsync_DownloadsRemoteFile_DoesNotOverwriteForDryrun()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();
            commandBase.Console = new TestConsole();
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "c.proto");

            // Act
            File.WriteAllText(tempProtoFile, "NonEquivalent Content");
            await commandBase.DownloadFileAsync(ProtoUrl, tempProtoFile, true);

            // Assert
            Assert.AreEqual("NonEquivalent Content", File.ReadAllText(tempProtoFile));
            File.Delete(tempProtoFile);
        }

        [Test]
        public void RemoveProtobufReference_RemovesReference_RetainsFile()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();
            commandBase.Console = new TestConsole();
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "c.proto");

            // Act
            File.WriteAllText(tempProtoFile, "Content");
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, tempProtoFile, "http://contoso.com/proto.proto");
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems("Protobuf");
            Assert.AreEqual(1, protoRefs.Count);

            // Act
            commandBase.RemoveProtobufReference(protoRefs.Single(), false);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            Assert.AreEqual(0, commandBase.Project.GetItems("Protobuf").Count);
            Assert.True(File.Exists(tempProtoFile));
            File.Delete(tempProtoFile);
        }

        [Test]
        public void RemoveProtobufReference_RemovesReference_DeletesFile()
        {
            // Arrange
            var commandBase = new CommandBase();
            commandBase.Project = new Project();
            commandBase.Console = new TestConsole();
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "c.proto");

            // Act
            File.WriteAllText(tempProtoFile, "Content");
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, tempProtoFile, "http://contoso.com/proto.proto");
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems("Protobuf");
            Assert.AreEqual(1, protoRefs.Count);

            // Act
            commandBase.RemoveProtobufReference(protoRefs.Single(), true);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            Assert.AreEqual(0, commandBase.Project.GetItems("Protobuf").Count);
            Assert.False(File.Exists(tempProtoFile));
        }
    }
}
