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

// Skip running load running tests in debug configuration
#if !DEBUG

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Grpc.AspNetCore.FunctionalTests.Linker.Helpers;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Linker;

[TestFixture]
[Category("LongRunning")]
public class LinkerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

#if NET9_0_OR_GREATER
    [Test]
    public async Task RunWebsiteAndCallWithClient_Aot_Success()
    {
        await RunWebsiteAndCallWithClient(publishAot: true);
    }

    [Test]
    public async Task RunWebsiteAndCallWithClient_Trimming_Success()
    {
        await RunWebsiteAndCallWithClient(publishAot: false);
    }
#endif

    private async Task RunWebsiteAndCallWithClient(bool publishAot)
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
            using var cts = new CancellationTokenSource();

            try
            {
                var publishWebsiteTask = PublishAppAsync(projectDirectory + @"\..\..\testassets\LinkerTestsWebsite\LinkerTestsWebsite.csproj", linkerTestsWebsitePath, publishAot, cts.Token);
                var publishClientTask = PublishAppAsync(projectDirectory + @"\..\..\testassets\LinkerTestsClient\LinkerTestsClient.csproj", linkerTestsClientPath, publishAot, cts.Token);

                await Task.WhenAll(publishWebsiteTask, publishClientTask).TimeoutAfter(Timeout);
                Console.WriteLine("Successfully published app.");
            }
            finally
            {
                cts.Dispose();
            }

            using var websiteProcess = new WebsiteProcess();
            using var clientProcess = new DotNetProcess();

            try
            {
                websiteProcess.Start(BuildStartPath(linkerTestsWebsitePath, "LinkerTestsWebsite"), arguments: null);
                await websiteProcess.WaitForReadyAsync().TimeoutAfter(Timeout);

                string? clientArguments = null;
                if (websiteProcess.ServerPort is {} serverPort)
                {
                    clientArguments = serverPort.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    throw new InvalidOperationException("Website server port not available.");
                }

                clientProcess.Start(BuildStartPath(linkerTestsClientPath, "LinkerTestsClient"), arguments: clientArguments);
                await clientProcess.WaitForExitAsync().TimeoutAfter(Timeout);
            }
            finally
            {
                Console.WriteLine("Website output:");
                Console.WriteLine(websiteProcess.GetOutput());
                Console.WriteLine("Client output:");
                Console.WriteLine(clientProcess.GetOutput());
            }

            Assert.AreEqual(0, clientProcess.ExitCode);
        }
        finally
        {
            EnsureDeleted(linkerTestsClientPath);
            EnsureDeleted(linkerTestsWebsitePath);
        }
    }

    private static string BuildStartPath(string path, string projectName)
    {
        // Executable on Windows has an *.exe extension.
        // We don't need to add it to the start path because *.exe is in the PATHEXT env var.
        return Path.Combine(path, projectName);
    }

    private static void EnsureDeleted(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static async Task PublishAppAsync(string path, string outputPath, bool publishAot, CancellationToken cancellationToken)
    {
        var resolvedPath = Path.GetFullPath(path);
        Console.WriteLine($"Publishing {resolvedPath}");

        var process = new DotNetProcess();
        cancellationToken.Register(() => process.Dispose());

        try
        {
            // The AppPublishAot parameter is used to tell the compiler to publish as AOT.
            // AppPublishAot is used instead of PublishAot because dependency projects have non-AOT targets. Setting "PublishAot=true" causes build errors.
            process.Start("dotnet", $"publish {resolvedPath} -r {GetRuntimeIdentifier()} -c Release -o {outputPath} -p:AppPublishAot={publishAot} --self-contained");
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
            return "win-" + architecture;
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

#endif
