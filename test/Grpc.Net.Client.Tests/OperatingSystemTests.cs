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

using Grpc.Net.Client.Internal;
using NUnit.Framework;
using OperatingSystem = Grpc.Net.Client.Internal.OperatingSystem;

namespace Grpc.Net.Client.Tests;

public class OperatingSystemTests
{
#if !NET5_0_OR_GREATER
    [Test]
    [Platform("Win", Reason = "Only runs on Windows where ntdll.dll is present.")]
    public void DetectWindowsVersion_Windows_MatchesEnvironment()
    {
        Native.DetectWindowsVersion(out var version, out _);

        // It is safe to compare Environment.OSVersion.Version on netfx because tests have no compatibility setting.
        Assert.AreEqual(Environment.OSVersion.Version, version);
    }

    [Test]
    [Platform("Win", Reason = "Only runs on Windows where ntdll.dll is present.")]
    public void InstanceAndIsWindowsServer_Windows_MatchesEnvironment()
    {
        Native.DetectWindowsVersion(out var version, out var isWindowsServer);

        Assert.AreEqual(true, OperatingSystem.Instance.IsWindows);
        Assert.AreEqual(version, OperatingSystem.Instance.OSVersion);
        Assert.AreEqual(isWindowsServer, OperatingSystem.Instance.IsWindowsServer);
    }
#endif

    [Test]
    public void OSVersion_ModernDotNet_MatchesEnvironment()
    {
        // It is safe to compare Environment.OSVersion.Version on netfx because tests have no compatibility setting.
        Assert.AreEqual(Environment.OSVersion.Version, OperatingSystem.Instance.OSVersion);
    }
}
