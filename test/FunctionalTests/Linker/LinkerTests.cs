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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Grpc.AspNetCore.FunctionalTests.Linker.Helpers;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Linker
{
    [TestFixture]
    [Category("LongRunning")]
    public class LinkerTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        [Test]
        public async Task RunWebsiteAndCallWithClient_Success()
        {
            var projectDirectory = typeof(LinkerTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "ProjectDirectory")
                .Value;

            var tempPath = Path.GetTempPath();
            var linkerTestsClientPath = Path.Combine(tempPath, "LinkerTestsClient");
            var linkerTestsWebsitePath = Path.Combine(tempPath, "LinkerTestsWebsite");

            EnsureDeleted(linkerTestsClientPath);
            EnsureDeleted(linkerTestsWebsitePath);

            try
            {
                using var websiteProcess = new WebsiteProcess();
                using var clientProcess = new DotNetProcess();

                try
                {
                    var publishWebsiteTask = PublishAppAsync(projectDirectory + @"\..\..\testassets\LinkerTestsWebsite\LinkerTestsWebsite.csproj", linkerTestsWebsitePath);
                    var publishClientTask = PublishAppAsync(projectDirectory + @"\..\..\testassets\LinkerTestsClient\LinkerTestsClient.csproj", linkerTestsClientPath);

                    await Task.WhenAll(publishWebsiteTask, publishClientTask).TimeoutAfter(Timeout);

                    websiteProcess.Start(Path.Combine(linkerTestsWebsitePath, "LinkerTestsWebsite.dll"));
                    await websiteProcess.WaitForReadyAsync().TimeoutAfter(Timeout);

                    clientProcess.Start(Path.Combine(linkerTestsClientPath, $"LinkerTestsClient.dll {websiteProcess.ServerPort}"));
                    await clientProcess.WaitForExitAsync().TimeoutAfter(Timeout);

                    Assert.AreEqual(0, clientProcess.ExitCode);
                }
                finally
                {
                    Console.WriteLine("Website output:");
                    Console.WriteLine(websiteProcess.GetOutput());
                    Console.WriteLine("Client output:");
                    Console.WriteLine(clientProcess.GetOutput());
                }
            }
            finally
            {
                EnsureDeleted(linkerTestsClientPath);
                EnsureDeleted(linkerTestsWebsitePath);
            }
        }

        private void EnsureDeleted(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private static async Task PublishAppAsync(string path, string outputPath)
        {
            var resolvedPath = Path.GetFullPath(path);
            Console.WriteLine($"Publishing {resolvedPath}");

            var process = new DotNetProcess();
            try
            {
                process.Start($"publish {resolvedPath} -r {GetRuntimeIdentifier()} -c Release -o {outputPath}");
                await process.WaitForExitAsync().TimeoutAfter(Timeout);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while publishing app.", ex);
            }
            finally
            {
                var exitCode = process.HasExited ? (int?)process.ExitCode : null;

                process.Dispose();
                
                var output = process.GetOutput();
                Console.WriteLine("Publish output:");
                Console.WriteLine(output);

                if (exitCode != null && exitCode.Value != 0)
                {
                    throw new Exception($"Non-zero exit code returned: {exitCode}");
                }
            }
        }

        private static string GetRuntimeIdentifier()
        {
            var architecture = RuntimeInformation.OSArchitecture.ToString().ToLower();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "win10-" + architecture;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "linux-" + architecture;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "osx-" + architecture;
            }
            throw new InvalidOperationException("Unrecognized operation system platform");
        }
    }
}
