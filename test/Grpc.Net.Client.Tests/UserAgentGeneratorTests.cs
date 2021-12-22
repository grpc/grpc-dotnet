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

using System.Runtime.InteropServices;
using Grpc.Net.Client.Internal;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class UserAgentGeneratorTests
    {
        [Test]
        public void HandlesNetFrameworkTarget()
        {
            var userAgent = UserAgentGenerator.GetUserAgentString(
                processArch: Architecture.X64,
                clrVersion: Version.Parse("5.0.7"),
                assemblyVersion: "2.4.1",
                runtimeInformation: ".NET 5.0.7",
                frameworkName: ".NETFramework,Version=v4.6.1",
                operatingSystem: "osx; ");
            Assert.AreEqual($"grpc-dotnet/2.4.1 (.NET 5.0.7; CLR 5.0.7; net461; osx; x64)", userAgent);
        }

        [Test]
        public void HandlesNetCoreAppTarget()
        {
            var userAgent = UserAgentGenerator.GetUserAgentString(
                processArch: Architecture.X64,
                clrVersion: Version.Parse("5.0.7"),
                assemblyVersion: "2.4.1",
                runtimeInformation: ".NET 5.0.7",
                frameworkName: ".NETCoreApp,Version=v2.2",
                operatingSystem: "osx; ");
            Assert.AreEqual($"grpc-dotnet/2.4.1 (.NET 5.0.7; CLR 5.0.7; netcoreapp2.2; osx; x64)", userAgent);
        }

        [Test]
        public void HandlesNetStandardTarget()
        {
            var userAgent = UserAgentGenerator.GetUserAgentString(
                processArch: Architecture.X64,
                clrVersion: Version.Parse("5.0.7"),
                assemblyVersion: "2.4.1",
                runtimeInformation: ".NET 5.0.7",
                frameworkName: ".NETStandard,Version=v2.0",
                operatingSystem: "osx; ");
            Assert.AreEqual($"grpc-dotnet/2.4.1 (.NET 5.0.7; CLR 5.0.7; netstandard2.0; osx; x64)", userAgent);
        }

        [Test]
        public void HandlesVersionWithGitHash()
        {
            var userAgent = UserAgentGenerator.GetUserAgentString(
                processArch: Architecture.X64,
                clrVersion: Version.Parse("5.0.7"),
                assemblyVersion: "2.4.1-dev+5325faf",
                runtimeInformation: ".NET 5.0.7",
                frameworkName: ".NETStandard,Version=v2.0",
                operatingSystem: "osx; ");
            Assert.AreEqual($"grpc-dotnet/2.4.1-dev (.NET 5.0.7; CLR 5.0.7; netstandard2.0; osx; x64)", userAgent);
        }

        [Test]
        public void HandlesNoVersion()
        {
            var userAgent = UserAgentGenerator.GetUserAgentString(
                processArch: Architecture.X64,
                clrVersion: Version.Parse("5.0.7"),
                assemblyVersion: string.Empty,
                runtimeInformation: ".NET 5.0.7",
                frameworkName: ".NETStandard,Version=v2.0",
                operatingSystem: "osx; ");
            Assert.AreEqual($"grpc-dotnet (.NET 5.0.7; CLR 5.0.7; netstandard2.0; osx; x64)", userAgent);
        }

        [Test]
        public void HandlesNoOperatingSystem()
        {
            var userAgent = UserAgentGenerator.GetUserAgentString(
                processArch: Architecture.X64,
                clrVersion: Version.Parse("5.0.7"),
                assemblyVersion: string.Empty,
                runtimeInformation: ".NET 5.0.7",
                frameworkName: ".NETStandard,Version=v2.0",
                operatingSystem: string.Empty);
            Assert.AreEqual($"grpc-dotnet (.NET 5.0.7; CLR 5.0.7; netstandard2.0; x64)", userAgent);
        }

        [Test]
        public void HandlesNoTargetFramework()
        {
            var userAgent = UserAgentGenerator.GetUserAgentString(
                processArch: Architecture.X64,
                clrVersion: Version.Parse("5.0.7"),
                assemblyVersion: string.Empty,
                runtimeInformation: ".NET 5.0.7",
                frameworkName: string.Empty,
                operatingSystem: "osx; ");
            Assert.AreEqual($"grpc-dotnet (.NET 5.0.7; CLR 5.0.7; osx; x64)", userAgent);
        }

        [Test]
        public void HandlesNoRuntimeInfo()
        {
            var userAgent = UserAgentGenerator.GetUserAgentString(
                processArch: Architecture.Arm64,
                clrVersion: Version.Parse("5.0.7"),
                assemblyVersion: string.Empty,
                runtimeInformation: string.Empty,
                frameworkName: string.Empty,
                operatingSystem: "windows; ");
            Assert.AreEqual($"grpc-dotnet (CLR 5.0.7; windows; arm64)", userAgent);
        }

        [Test]
        public void HandlesMonoRuntimeInfo()
        {
            var userAgent = UserAgentGenerator.GetUserAgentString(
                processArch: Architecture.X64,
                clrVersion: Version.Parse("4.0.30319"),
                assemblyVersion: string.Empty,
                runtimeInformation: "Mono 6.12.0.140 (2020-02/51d876a041e Thu Apr 29 10:44:55 EDT 2021)",
                frameworkName: string.Empty,
                operatingSystem: "osx; ");
            Assert.AreEqual($"grpc-dotnet (Mono 6.12.0.140; CLR 4.0.30319; osx; x64)", userAgent);

        }
    }
}