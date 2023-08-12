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

namespace Grpc.Net.Client.Internal;

internal interface IOperatingSystem
{
    bool IsBrowser { get; }
    bool IsAndroid { get; }
    bool IsWindows { get; }
    Version OSVersion { get; }
}

internal sealed class OperatingSystem : IOperatingSystem
{
    public static readonly OperatingSystem Instance = new OperatingSystem();

    public bool IsBrowser { get; }
    public bool IsAndroid { get; }
    public bool IsWindows { get; }
    public Version OSVersion { get; }

    private OperatingSystem()
    {
#if NET5_0_OR_GREATER
        IsAndroid = System.OperatingSystem.IsAndroid();
        IsWindows = System.OperatingSystem.IsWindows();
        IsBrowser = System.OperatingSystem.IsBrowser();
        OSVersion = Environment.OSVersion.Version;
#else
        IsAndroid = false;
        IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        IsBrowser = RuntimeInformation.IsOSPlatform(OSPlatform.Create("browser"));

        // Older versions of .NET report an OSVersion.Version based on Windows compatibility settings.
        // For example, if an app running on Windows 11 is configured to be "compatible" with Windows 10
        // then the version returned is always Windows 10.
        //
        // Get correct Windows version directly from Windows by calling RtlGetVersion.
        // https://www.pinvoke.net/default.aspx/ntdll/RtlGetVersion.html
        OSVersion = IsWindows ? NtDll.DetectWindowsVersion() : Environment.OSVersion.Version;
#endif
    }
}
