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
    public class ListCommandTests : TestBase
    {
        [Test]
        [Ignore("https://github.com/grpc/grpc-dotnet/issues/457")]
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
                var command = new ListCommand(testConsole, null);
                command.List();

                // Assert
                var output = testConsole.Out.ToString()!;
                var lines = output.Split(Environment.NewLine);

                // First line is the heading and should conatin Protobuf Reference, Service Type, Source URL, Access
                Assert.True(lines[0].Contains("Protobuf Reference"));
                Assert.True(lines[0].Contains("Service Type"));
                Assert.True(lines[0].Contains("Source URL"));
                Assert.True(lines[0].Contains("Access"));

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
    }
}
