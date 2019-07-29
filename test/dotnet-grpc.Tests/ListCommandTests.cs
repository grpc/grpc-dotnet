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
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Commands;
using Grpc.Dotnet.Cli.Properties;
using NUnit.Framework;

namespace Grpc.Dotnet.Cli.Tests
{
    [TestFixture]
    public class ListCommandTests : TestBase
    {
        [NonParallelizable]
        [Test]
        public void List_ListsReferences()
        {
            // Arrange
            var currentDir = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var testConsole = new TestConsole();
            new DirectoryInfo(Path.Combine(currentDir, "TestAssets", "MultipleReferences")).CopyTo(tempDir);

            // Act
            Directory.SetCurrentDirectory(tempDir);
            var command = new ListCommand(testConsole, null);
            command.List();

            // Assert
            var output = testConsole.Out.ToString()!;
            Assert.True(output.Contains("URL reference: Proto/a.proto from https://contoso.com/greet.proto"));
            Assert.True(output.Contains("File reference: Proto/b.proto"));

            // Cleanup
            Directory.SetCurrentDirectory(currentDir);
            Directory.Delete(tempDir, true);
        }
    }
}
