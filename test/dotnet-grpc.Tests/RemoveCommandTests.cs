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

using System;
using System.CommandLine;
using System.IO;
using Grpc.Dotnet.Cli.Commands;
using NUnit.Framework;

namespace Grpc.Dotnet.Cli.Tests
{
    [TestFixture]
    public class RemoveCommandTests : TestBase
    {
        [Test]
        [NonParallelizable]
        public void Remove_RemovesReferences()
        {
            // Arrange
            var currentDir = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "ProjectWithReference")).CopyTo(tempDir);

            // Act
            Directory.SetCurrentDirectory(tempDir);
            var command = new RemoveCommand(new TestConsole(), null);
            command.Remove(new[] { Path.Combine("Proto", "a.proto") });

            // Assert
            var protoRefs = command.Project.GetItems(CommandBase.ProtobufElement);
            Assert.AreEqual(0, protoRefs.Count);
            Assert.True(File.Exists(Path.Combine(command.Project.DirectoryPath, "Proto", "a.proto")));

            // Cleanup
            Directory.SetCurrentDirectory(currentDir);
            Directory.Delete(tempDir, true);
        }
    }
}
