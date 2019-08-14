﻿#region Copyright notice and license

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

using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Commands;
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Dotnet.Cli.Tests
{
    [TestFixture]
    public class AddUrlCommandTests : TestBase
    {
        [Test]
        [NonParallelizable]
        public async Task AddUrlCommand_AddsPackagesAndReferences()
        {
            // Arrange
            var currentDir = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "EmptyProject")).CopyTo(tempDir);

            // Act
            Directory.SetCurrentDirectory(tempDir);
            var command = new AddUrlCommand(new TestConsole(), CreateClient());
            await command.AddUrlAsync(Services.Server, Access.Internal, "ImportDir", SourceUrl, Path.Combine("Proto", "c.proto"));
            command.Project.ReevaluateIfNecessary();

            // Assert
            var packageRefs = command.Project.GetItems(CommandBase.PackageReferenceElement);
            Assert.AreEqual(1, packageRefs.Count);
            Assert.NotNull(packageRefs.SingleOrDefault(r => r.UnevaluatedInclude == "Grpc.AspNetCore" && !r.HasMetadata(CommandBase.PrivateAssetsElement)));

            var protoRefs = command.Project.GetItems(CommandBase.ProtobufElement);
            Assert.AreEqual(1, protoRefs.Count);
            var protoRef = protoRefs.Single();
            Assert.AreEqual(Path.Combine("Proto", "c.proto"), protoRef.UnevaluatedInclude);
            Assert.AreEqual("Server", protoRef.GetMetadataValue(CommandBase.GrpcServicesElement));
            Assert.AreEqual("ImportDir", protoRef.GetMetadataValue(CommandBase.AdditionalImportDirsElement));
            Assert.AreEqual("Internal", protoRef.GetMetadataValue(CommandBase.AccessElement));
            Assert.AreEqual(SourceUrl, protoRef.GetMetadataValue(CommandBase.SourceUrlElement));

            Assert.IsNotEmpty(File.ReadAllText(Path.Combine(command.Project.DirectoryPath, "Proto", "c.proto")));

            // Cleanup
            Directory.SetCurrentDirectory(currentDir);
            Directory.Delete(tempDir, true);
        }

        [Test]
        public async Task AddUrlCommand_NoOutputSpecified_Error()
        {
            // Arrange
            var currentDir = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "EmptyProject")).CopyTo(tempDir);

            // Act, Assert
            Directory.SetCurrentDirectory(tempDir);
            var command = new AddUrlCommand(new TestConsole(), CreateClient());
            await ExceptionAssert.ThrowsAsync<CLIToolException>(() => command.AddUrlAsync(Services.Server, Access.Internal, "ImportDir", SourceUrl, string.Empty)).DefaultTimeout();

            // Cleanup
            Directory.SetCurrentDirectory(currentDir);
            Directory.Delete(tempDir, true);
        }
    }
}
