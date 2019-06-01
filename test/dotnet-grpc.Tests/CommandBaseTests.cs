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
using System.Text;
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
        private static readonly string ProtoUrl = "https://contoso.com/greet.proto";
        private static readonly string ProtoContent = @"// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the ""License"");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an ""AS IS"" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

syntax = ""proto3"";

package Greet;

// The greeting service definition.
service Greeter {
  // Sends a greeting
  rpc SayHello (HelloRequest) returns (HelloReply) {}
  rpc SayHellos (HelloRequest) returns (stream HelloReply) {}
}

// The request message containing the user's name.
message HelloRequest {
  string name = 1;
}

// The response message containing the greetings
message HelloReply {
  string message = 1;
}";

        [OneTimeSetUp]
        public void Initialize()
        {
            MSBuildLocator.RegisterDefaults();
            CommandBase.GetStreamAsync = url => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(ProtoContent)));
        }

        [Test]
        public void EnsureNugetPackages_AddsRequiredPackages()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());

            // Act
            commandBase.EnsureNugetPackages();
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var packageRefs = commandBase.Project.GetItems(CommandBase.PackageReferenceElement);
            Assert.AreEqual(3, packageRefs.Count);
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Google.Protobuf" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.AspNetCore.Server" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.Tools" && r.HasMetadata(CommandBase.PrivateAssetsElement)));
        }

        [Test]
        public void EnsureNugetPackages_DoesNotOverwriteExistingPackageReferences()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());
            commandBase.Project.AddItem(CommandBase.PackageReferenceElement, "Grpc.Tools");

            // Act
            commandBase.EnsureNugetPackages();
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var packageRefs = commandBase.Project.GetItems(CommandBase.PackageReferenceElement);
            Assert.AreEqual(3, packageRefs.Count);
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Google.Protobuf" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.AspNetCore.Server" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.Tools" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
        }

        [Test]
        public void AddProtobufReference_ThrowsIfFileNotFound()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());

            // Act, Assert
            Assert.Throws<CLIToolException>(() => commandBase.AddProtobufReference(Services.Both, string.Empty, Access.Public, "NonExistentFile", string.Empty));
        }

        [Test]
        public void AddProtobufReference_AddsRelativeReference()
        {
            // Arrange
            var commandBase = new CommandBase(
                new TestConsole(),
                Project.FromFile(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "test.csproj"), new ProjectOptions { ProjectCollection = new ProjectCollection() }));

            var referencePath = Path.Combine("Proto", "a.proto");

            // Act
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, referencePath, "http://contoso.com/proto.proto");
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems(CommandBase.ProtobufElement);
            Assert.AreEqual(1, protoRefs.Count);
            var protoRef = protoRefs.Single();
            Assert.AreEqual(referencePath, protoRef.UnevaluatedInclude);
            Assert.AreEqual("Server", protoRef.GetMetadataValue(CommandBase.GrpcServicesElement));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue(CommandBase.AdditionalImportDirsElement));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue(CommandBase.AccessElement));
            Assert.AreEqual("http://contoso.com/proto.proto", protoRef.GetMetadataValue(CommandBase.SourceUrlElement));
        }

        [Test]
        public void AddProtobufReference_AddsAbsoluteReference()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());
            var referencePath = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "a.proto");

            // Act
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, referencePath, "http://contoso.com/proto.proto");
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems(CommandBase.ProtobufElement);
            Assert.AreEqual(1, protoRefs.Count);
            var protoRef = protoRefs.Single();
            Assert.AreEqual(referencePath, protoRef.UnevaluatedInclude);
            Assert.AreEqual("Server", protoRef.GetMetadataValue(CommandBase.GrpcServicesElement));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue(CommandBase.AdditionalImportDirsElement));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue(CommandBase.AccessElement));
            Assert.AreEqual("http://contoso.com/proto.proto", protoRef.GetMetadataValue(CommandBase.SourceUrlElement));
        }

        [Test]
        public void AddProtobufReference_DoesNotOverwriteReference()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());
            var referencePath = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "a.proto");

            // Act
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, referencePath, "http://contoso.com/proto.proto");
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems(CommandBase.ProtobufElement);
            Assert.AreEqual(1, protoRefs.Count);
            var protoRef = protoRefs.Single();
            Assert.AreEqual(referencePath, protoRef.UnevaluatedInclude);
            Assert.AreEqual("Server", protoRef.GetMetadataValue(CommandBase.GrpcServicesElement));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue(CommandBase.AdditionalImportDirsElement));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue(CommandBase.AccessElement));
            Assert.AreEqual("http://contoso.com/proto.proto", protoRef.GetMetadataValue(CommandBase.SourceUrlElement));
        }

        [Test]
        public void ResolveProject_ThrowsIfProjectFileDoesNotExist()
        {
            // Act, Assert
            Assert.Throws<CLIToolException>(() => CommandBase.ResolveProject(new FileInfo("NonExistent")));
        }

        [Test]
        [NonParallelizable]
        public void ResolveProject_SucceedIfOnlyOneProject()
        {
            // Arrange
            var currentDirectory = Directory.GetCurrentDirectory();
            var testAssetsDirectory = Path.Combine(currentDirectory, "TestAssets");
            Directory.SetCurrentDirectory(testAssetsDirectory);

            // Act
            var project = CommandBase.ResolveProject(null);

            // Assert
            Assert.AreEqual(Path.Combine(testAssetsDirectory, "test.csproj"), project.FullPath);
            Directory.SetCurrentDirectory(currentDirectory);
        }

        [Test]
        [NonParallelizable]
        public void ResolveProject_ThrowsIfMoreThanOneProjectFound()
        {
            // Arrange
            var currentDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Path.Combine(currentDirectory, "TestAssets", "DuplicateProjects"));

            // Act, Assert
            Assert.Throws<CLIToolException>(() => CommandBase.ResolveProject(null));
            Directory.SetCurrentDirectory(currentDirectory);
        }

        [Test]
        [NonParallelizable]
        public void ResolveProject_ThrowsIfZeroProjectFound()
        {
            // Arrange
            var currentDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Path.Combine(currentDirectory, "TestAssets", "Proto"));

            // Act, Assert
            Assert.Throws<CLIToolException>(() => CommandBase.ResolveProject(null));
            Directory.SetCurrentDirectory(currentDirectory);
        }

        [Test]
        public void GlobReferences_ExpandsRelativeReferences()
        {
            // Arrange
            var commandBase = new CommandBase(
                new TestConsole(),
                Project.FromFile(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "test.csproj"), new ProjectOptions { ProjectCollection = new ProjectCollection() }));

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
            var commandBase = new CommandBase(new TestConsole(), new Project());

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
            var commandBase = new CommandBase(new TestConsole(), new Project());
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
            var commandBase = new CommandBase(new TestConsole(), new Project());
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
            var commandBase = new CommandBase(new TestConsole(), new Project());
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
            var commandBase = new CommandBase(new TestConsole(), new Project());
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
            var commandBase = new CommandBase(new TestConsole(), new Project());
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "c.proto");

            // Act
            File.WriteAllText(tempProtoFile, "Content");
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, tempProtoFile, "http://contoso.com/proto.proto");
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems(CommandBase.ProtobufElement);
            Assert.AreEqual(1, protoRefs.Count);

            // Act
            commandBase.RemoveProtobufReference(protoRefs.Single(), false);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            Assert.AreEqual(0, commandBase.Project.GetItems(CommandBase.ProtobufElement).Count);
            Assert.True(File.Exists(tempProtoFile));
            File.Delete(tempProtoFile);
        }

        [Test]
        public void RemoveProtobufReference_RemovesReference_DeletesFile()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "Proto", "c.proto");

            // Act
            File.WriteAllText(tempProtoFile, "Content");
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, tempProtoFile, "http://contoso.com/proto.proto");
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems(CommandBase.ProtobufElement);
            Assert.AreEqual(1, protoRefs.Count);

            // Act
            commandBase.RemoveProtobufReference(protoRefs.Single(), true);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            Assert.AreEqual(0, commandBase.Project.GetItems(CommandBase.ProtobufElement).Count);
            Assert.False(File.Exists(tempProtoFile));
        }

        [TestCase("http://contoso.com/file.proto", true)]
        [TestCase("https://contoso.com/file.proto", true)]
        [TestCase("HTTPS://contoso.com/FILE.PROTO", true)]
        [TestCase("C:\\contoso.com\\FILE.PROTO", false)]
        [TestCase("FILE.PROTO", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsUrl_ChecksUrlValidity(string url, bool isUrl)
        {
            Assert.AreEqual(isUrl, CommandBase.IsUrl(url));
        }
    }
}
