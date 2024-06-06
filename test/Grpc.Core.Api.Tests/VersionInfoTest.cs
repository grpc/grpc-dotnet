#region Copyright notice and license

// Copyright 2022 The gRPC Authors
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

using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;

namespace Grpc.Core.Tests;

public class VersionInfoTest
{
    [Test]
    public void VersionInfoMatchesAssemblyProperties()
    {
        var assembly = typeof(Status).Assembly;  // the Grpc.Core.Api assembly

        var assemblyVersion = assembly!.GetName()!.Version!.ToString()!;
        Assert.AreEqual(VersionInfo.CurrentAssemblyVersion, assemblyVersion);

        string assemblyFileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion!;
        string assemblyFileVersionFromAttribute = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version;
        Assert.AreEqual(VersionInfo.CurrentAssemblyFileVersion, assemblyFileVersion);
        Assert.AreEqual(VersionInfo.CurrentAssemblyFileVersion, assemblyFileVersionFromAttribute);

        string productVersion = FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion!;
        string informationalVersionFromAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.AreEqual(productVersion, informationalVersionFromAttribute);
        // grpc-dotnet appends commit SHA to the product version (e.g. "2.45.0-dev+e30038495bd26b812b6684049353c045d1049d3c")
        string productVersionWithoutCommitSha = productVersion.Split('+')[0];
        Assert.AreEqual(VersionInfo.CurrentVersion, productVersionWithoutCommitSha);
    }
}
