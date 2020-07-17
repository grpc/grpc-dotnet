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

using System.Collections.Generic;
using System.CommandLine.IO;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Commands;
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Dotnet.Cli.Properties;
using Grpc.Tests.Shared;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using NUnit.Framework;

namespace Grpc.Dotnet.Cli.Tests
{
    [TestFixture]
    public class CommandBaseTests : TestBase
    {

        [Test]
        public Task EnsureNugetPackages_AddsRequiredServerPackages_ForServer()
            => EnsureNugetPackages_AddsRequiredServerPackages(Services.Server);

        [Test]
        public Task EnsureNugetPackages_AddsRequiredServerPackages_ForBoth()
            => EnsureNugetPackages_AddsRequiredServerPackages(Services.Both);

        private async Task EnsureNugetPackages_AddsRequiredServerPackages(Services services)
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());

            // Act
            await commandBase.EnsureNugetPackagesAsync(services);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var packageRefs = commandBase.Project.GetItems(CommandBase.PackageReferenceElement);
            Assert.AreEqual(1, packageRefs.Count);
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.AspNetCore" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
        }

        [Test]
        public async Task EnsureNugetPackages_AddsRequiredClientPackages_ForNonWebClient()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());

            // Act
            await commandBase.EnsureNugetPackagesAsync(Services.Client);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var packageRefs = commandBase.Project.GetItems(CommandBase.PackageReferenceElement);
            Assert.AreEqual(3, packageRefs.Count);
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Google.Protobuf" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.Net.ClientFactory" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.Tools" && r.HasMetadata(CommandBase.PrivateAssetsElement)));
        }

        [Test]
        public async Task EnsureNugetPackages_AddsRequiredClientPackages_ForWebClient()
        {
            // Arrange
            var commandBase = new CommandBase(
                new TestConsole(),
                CreateIsolatedProject(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "test.csproj")));

            // Act
            await commandBase.EnsureNugetPackagesAsync(Services.Client);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var packageRefs = commandBase.Project.GetItems(CommandBase.PackageReferenceElement);
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Google.Protobuf" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.AspNetCore.Server.ClientFactory" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.Tools" && r.HasMetadata(CommandBase.PrivateAssetsElement)));
        }

        [Test]
        public async Task EnsureNugetPackages_AddsRequiredNonePackages_ForNone()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());

            // Act
            await commandBase.EnsureNugetPackagesAsync(Services.None);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var packageRefs = commandBase.Project.GetItems(CommandBase.PackageReferenceElement);
            Assert.AreEqual(2, packageRefs.Count);
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Google.Protobuf" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.Tools" && r.HasMetadata(CommandBase.PrivateAssetsElement)));
        }

        [Test]
        public async Task EnsureNugetPackages_DoesNotOverwriteExistingPackageReferences()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());
            commandBase.Project.AddItem(CommandBase.PackageReferenceElement, "Grpc.Tools");

            // Act
            await commandBase.EnsureNugetPackagesAsync(Services.None);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var packageRefs = commandBase.Project.GetItems(CommandBase.PackageReferenceElement);
            Assert.AreEqual(2, packageRefs.Count);
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Google.Protobuf" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));
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
                CreateIsolatedProject(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "test.csproj")));

            var referencePath = Path.Combine("Proto", "a.proto");

            // Act
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, referencePath, SourceUrl);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems(CommandBase.ProtobufElement);
            Assert.AreEqual(1, protoRefs.Count);
            var protoRef = protoRefs.Single();
            Assert.AreEqual(referencePath, protoRef.UnevaluatedInclude);
            Assert.AreEqual("Server", protoRef.GetMetadataValue(CommandBase.GrpcServicesElement));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue(CommandBase.AdditionalImportDirsElement));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue(CommandBase.AccessElement));
            Assert.AreEqual(SourceUrl, protoRef.GetMetadataValue(CommandBase.SourceUrlElement));
            Assert.False(protoRef.HasMetadata(CommandBase.LinkElement));
        }

        [Test]
        public void AddProtobufReference_AddsAbsoluteReference()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());
            var referencePath = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto", "a.proto");

            // Act
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, referencePath, SourceUrl);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems(CommandBase.ProtobufElement);
            Assert.AreEqual(1, protoRefs.Count);
            var protoRef = protoRefs.Single();
            Assert.AreEqual(referencePath, protoRef.UnevaluatedInclude);
            Assert.AreEqual("Server", protoRef.GetMetadataValue(CommandBase.GrpcServicesElement));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue(CommandBase.AdditionalImportDirsElement));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue(CommandBase.AccessElement));
            Assert.AreEqual(SourceUrl, protoRef.GetMetadataValue(CommandBase.SourceUrlElement));
            Assert.False(protoRef.HasMetadata(CommandBase.LinkElement));
        }

        [Test]
        public void AddProtobufReference_DoesNotOverwriteReference()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());
            var referencePath = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto", "a.proto");

            // Act
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, referencePath, SourceUrl);
            commandBase.AddProtobufReference(Services.Client, "ImportDir2", Access.Public, referencePath, SourceUrl + ".proto");
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems(CommandBase.ProtobufElement);
            Assert.AreEqual(1, protoRefs.Count);
            var protoRef = protoRefs.Single();
            Assert.AreEqual(referencePath, protoRef.UnevaluatedInclude);
            Assert.AreEqual("Server", protoRef.GetMetadataValue(CommandBase.GrpcServicesElement));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue(CommandBase.AdditionalImportDirsElement));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue(CommandBase.AccessElement));
            Assert.AreEqual(SourceUrl, protoRef.GetMetadataValue(CommandBase.SourceUrlElement));
        }

        static object[] ProtosOutsideProject =
        {
            new object[] { Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "ProjectWithReference", "Proto", "a.proto") },
            new object[] { Path.Combine("..", "ProjectWithReference", "Proto", "a.proto") },
        };

        [Test]
        [TestCaseSource("ProtosOutsideProject")]
        public void AddProtobufReference_AddsLinkElementIfFileOutsideProject(string reference)
        {
            // Arrange
            var commandBase = new CommandBase(
                new TestConsole(),
                CreateIsolatedProject(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "test.csproj")));

            // Act
            commandBase.AddProtobufReference(Services.Server, "ImportDir", Access.Internal, reference, SourceUrl);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var protoRefs = commandBase.Project.GetItems(CommandBase.ProtobufElement);
            Assert.AreEqual(1, protoRefs.Count);
            var protoRef = protoRefs.Single();
            Assert.AreEqual(reference, protoRef.UnevaluatedInclude);
            Assert.AreEqual("Server", protoRef.GetMetadataValue(CommandBase.GrpcServicesElement));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue(CommandBase.AdditionalImportDirsElement));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue(CommandBase.AccessElement));
            Assert.AreEqual(SourceUrl, protoRef.GetMetadataValue(CommandBase.SourceUrlElement));
            Assert.AreEqual(Path.Combine(CommandBase.ProtosFolder, "a.proto"), protoRef.GetMetadataValue(CommandBase.LinkElement));
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
            var testAssetsDirectory = Path.Combine(currentDirectory, "TestAssets", "EmptyProject");
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
            Directory.SetCurrentDirectory(Path.Combine(currentDirectory, "TestAssets", "EmptyProject", "Proto"));

            // Act, Assert
            Assert.Throws<CLIToolException>(() => CommandBase.ResolveProject(null));
            Directory.SetCurrentDirectory(currentDirectory);
        }

        [Test]
        public void ResolveServices_ReturnsIdentity_Both()
            => ResolveServices_ReturnsIdentity_IfNotDefault(Services.Both);

        [Test]
        public void ResolveServices_ReturnsIdentity_Server()
            => ResolveServices_ReturnsIdentity_IfNotDefault(Services.Server);

        [Test]
        public void ResolveServices_ReturnsIdentity_Client()
            => ResolveServices_ReturnsIdentity_IfNotDefault(Services.Client);

        [Test]
        public void ResolveServices_ReturnsIdentity_None()
            => ResolveServices_ReturnsIdentity_IfNotDefault(Services.None);

        private void ResolveServices_ReturnsIdentity_IfNotDefault(Services services)
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project());

            // Act, Assert
            Assert.AreEqual(services, commandBase.ResolveServices(services));
        }

        [Test]
        public void ResolveServices_ReturnsBoth_IfWebSDK()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), CreateIsolatedProject(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "DuplicateProjects", "server.csproj")));

            // Act, Assert
            Assert.AreEqual(Services.Both, commandBase.ResolveServices(Services.Default));
        }

        [Test]
        public void ResolveServices_ReturnsClient_IfNotWebSDK()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), CreateIsolatedProject(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "DuplicateProjects", "client.csproj")));

            // Act, Assert
            Assert.AreEqual(Services.Client, commandBase.ResolveServices(Services.Default));
        }

        [Test]
        public void GlobReferences_ExpandsRelativeReferences_WarnsIfReferenceNotResolved()
        {
            // Arrange
            var testConsole = new TestConsole();
            var commandBase = new CommandBase(
                testConsole,
                CreateIsolatedProject(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "test.csproj")));
            var invalidReference = Path.Combine("Proto", "invalid*reference.proto");

            // Act
            var references = commandBase.GlobReferences(new[] { Path.Combine("Proto", "*.proto"), invalidReference });

            // Assert
            Assert.Contains(Path.Combine("Proto", "a.proto"), references);
            Assert.Contains(Path.Combine("Proto", "b.proto"), references);
            Assert.AreEqual($"Warning: {string.Format(CoreStrings.LogWarningNoReferenceResolved, invalidReference, SourceUrl)}", testConsole.Out.ToString()!.TrimEnd());
        }

        [Test]
        public void GlobReferences_ExpandsAbsoluteReferences_WarnsIfReferenceNotResolved()
        {
            // Arrange
            var testConsole = new TestConsole();
            var commandBase = new CommandBase(testConsole, new Project());
            var invalidReference = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto", "invalid*reference.proto");

            // Act
            var references = commandBase.GlobReferences(new[] { Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto", "*.proto"), invalidReference });

            // Assert
            Assert.Contains(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto", "a.proto"), references);
            Assert.Contains(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto", "b.proto"), references);
            Assert.AreEqual($"Warning: {string.Format(CoreStrings.LogWarningNoReferenceResolved, invalidReference, SourceUrl)}", testConsole.Out.ToString()!.TrimEnd());
        }

        static object[] DirectoryPaths =
        {
            new object[] { Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto") },
            new object[] { Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto") + Path.DirectorySeparatorChar) },
            new object[] { Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto") + Path.AltDirectorySeparatorChar) },
        };

        [TestCaseSource("DirectoryPaths")]
        public async Task DownloadFileAsync_DirectoryAsDestination_Throws(string destination)
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project(), CreateClient());

            // Act, Assert
            await ExceptionAssert.ThrowsAsync<CLIToolException>(() => commandBase.DownloadFileAsync(SourceUrl, destination)).DefaultTimeout();
        }

        [Test]
        public async Task DownloadFileAsync_DownloadsRemoteFile()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project(), CreateClient());
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto", "c.proto");

            // Act
            await commandBase.DownloadFileAsync(SourceUrl, tempProtoFile).DefaultTimeout();

            // Assert
            Assert.IsNotEmpty(File.ReadAllText(tempProtoFile));
            File.Delete(tempProtoFile);
        }

        [Test]
        public async Task DownloadFileAsync_DownloadsRemoteFile_OverwritesIfContentDoesNotMatch()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project(), CreateClient());
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto", "c.proto");

            // Act
            File.WriteAllText(tempProtoFile, "NonEquivalent Content");
            await commandBase.DownloadFileAsync(SourceUrl, tempProtoFile);

            // Assert
            Assert.AreNotEqual("NonEquivalent Content", File.ReadAllText(tempProtoFile));
            File.Delete(tempProtoFile);
        }

        [Test]
        public async Task DownloadFileAsync_DownloadsRemoteFile_SkipIfContentMatches()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project(), CreateClient());
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto", "c.proto");

            // Act
            await commandBase.DownloadFileAsync(SourceUrl, tempProtoFile);
            var lastWriteTime = File.GetLastWriteTime(tempProtoFile);
            await commandBase.DownloadFileAsync(SourceUrl, tempProtoFile);

            // Assert
            Assert.AreEqual(lastWriteTime, File.GetLastWriteTime(tempProtoFile));
            File.Delete(tempProtoFile);
        }

        [Test]
        public async Task DownloadFileAsync_DownloadsRemoteFile_DoesNotOverwriteForDryrun()
        {
            // Arrange
            var commandBase = new CommandBase(new TestConsole(), new Project(), CreateClient());
            var tempProtoFile = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets", "EmptyProject", "Proto", "c.proto");

            // Act
            File.WriteAllText(tempProtoFile, "NonEquivalent Content");
            await commandBase.DownloadFileAsync(SourceUrl, tempProtoFile, true);

            // Assert
            Assert.AreEqual("NonEquivalent Content", File.ReadAllText(tempProtoFile));
            File.Delete(tempProtoFile);
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

        private Project CreateIsolatedProject(string path)
            => Project.FromFile(path, new ProjectOptions { ProjectCollection = new ProjectCollection() });
    }

    [TestFixture]
    public class CommandBaseRemoteFileTests : TestBase
    {
        [Test]
        public async Task EnsureNugetPackages_UsesVersionsFromRemoteFile_IfAvailable()
        {
            // Arrange
            var content = new Dictionary<string, string>()
            {
                // Dummy entry for package version file
                {
                    CommandBase.PackageVersionUrl,
                    // Client package versions are omitted to model missing package information
                    @"{
                      ""Version"" : ""1.0"",
                      ""Packages""  :  {
                        ""Microsoft.Azure.SignalR"": ""1.1.0-preview1-10442"",
                        ""Grpc.AspNetCore"": ""1.2.3"",
                        ""Google.Protobuf"": ""4.5.6"",
                        ""Grpc.Tools"": ""7.8.9"",
                        ""NSwag.ApiDescription.Client"": ""13.0.3"",
                        ""Microsoft.Extensions.ApiDescription.Client"": ""0.3.0-preview7.19365.7"",
                        ""Newtonsoft.Json"": ""12.0.2""
                      }
                    }"
                }
            };
            var commandBase = new CommandBase(new TestConsole(), new Project(), CreateClient(content));

            // Act
            await commandBase.EnsureNugetPackagesAsync(Services.Client);
            commandBase.Project.ReevaluateIfNecessary();

            // Assert
            var packageRefs = commandBase.Project.GetItems(CommandBase.PackageReferenceElement);
            var protobufReference = packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Google.Protobuf");
            Assert.NotNull(protobufReference);
            Assert.AreEqual("4.5.6", protobufReference!.GetMetadataValue("Version"));
            var toolsReference = packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.Tools");
            Assert.NotNull(toolsReference);
            Assert.AreEqual("7.8.9", toolsReference!.GetMetadataValue("Version"));
            var clientFactoryReference = packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.Net.ClientFactory");
            Assert.NotNull(clientFactoryReference);
            var clientFactoryVersion = GetType().Assembly
                .GetCustomAttributes<GrpcDependencyAttribute>()
                .Select(a => a as GrpcDependencyAttribute)
                .Single(a => a.Name == "Grpc.Net.ClientFactory")
                .Version;
            Assert.AreEqual(clientFactoryVersion, clientFactoryReference!.GetMetadataValue("Version"));
        }
    }
}
