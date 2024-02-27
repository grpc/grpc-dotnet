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
    bool IsWindowsServer { get; }
    Version OSVersion { get; }
}

internal sealed class OperatingSystem : IOperatingSystem
{
    public static readonly OperatingSystem Instance = new OperatingSystem();

    private readonly Lazy<bool> _isWindowsServer;

    public bool IsBrowser { get; }
    public bool IsAndroid { get; }
    public bool IsWindows { get; }
    public bool IsWindowsServer => _isWindowsServer.Value;
    public Version OSVersion { get; }

    private OperatingSystem()
    {
#if NET5_0_OR_GREATER
        IsAndroid = System.OperatingSystem.IsAndroid();
        IsWindows = System.OperatingSystem.IsWindows();
        IsBrowser = System.OperatingSystem.IsBrowser();
        OSVersion = Environment.OSVersion.Version;

        // Windows Server detection requires a P/Invoke call to RtlGetVersion.
        // Get the value lazily so that it is only called if needed.
        _isWindowsServer = new Lazy<bool>(() =>
        {
            // RtlGetVersion is not available on UWP. Check it first.
            if (IsWindows && !Native.IsUwp(RuntimeInformation.FrameworkDescription, Environment.OSVersion.Version))
            {
                Native.DetectWindowsVersion(out _, out var isWindowsServer);
                return isWindowsServer;
            }

            return false;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
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
        //
        // RtlGetVersion is not available on UWP. Check it first.
        if (IsWindows && !Native.IsUwp(RuntimeInformation.FrameworkDescription, Environment.OSVersion.Version))
        {
            Native.DetectWindowsVersion(out var windowsVersion, out var windowsServer);
            OSVersion = windowsVersion;
            _isWindowsServer = new Lazy<bool>(() => windowsServer, LazyThreadSafetyMode.ExecutionAndPublication);
        }
        else
        {
            OSVersion = Environment.OSVersion.Version;
            _isWindowsServer = new Lazy<bool>(() => false, LazyThreadSafetyMode.ExecutionAndPublication);
        }
#endif
    }
}
