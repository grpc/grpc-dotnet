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

/// <summary>
/// Types for calling RtlGetVersion. See https://www.pinvoke.net/default.aspx/ntdll/RtlGetVersion.html
/// </summary>
internal static class NtDll
{
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
    [DllImport("ntdll.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern NTSTATUS RtlGetVersion(ref OSVERSIONINFOEX versionInfo);
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

    internal static void DetectWindowsVersion(out Version version, out bool isWindowsServer)
    {
        // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-osversioninfoexa
        const byte VER_NT_SERVER = 3;

        var osVersionInfo = new OSVERSIONINFOEX { OSVersionInfoSize = Marshal.SizeOf<OSVERSIONINFOEX>() };

        if (RtlGetVersion(ref osVersionInfo) != NTSTATUS.STATUS_SUCCESS)
        {
            throw new InvalidOperationException($"Failed to call internal {nameof(RtlGetVersion)}.");
        }

        version = new Version(osVersionInfo.MajorVersion, osVersionInfo.MinorVersion, osVersionInfo.BuildNumber, 0);
        isWindowsServer = osVersionInfo.ProductType == VER_NT_SERVER;
    }

    internal enum NTSTATUS : uint
    {
        /// <summary>
        /// The operation completed successfully. 
        /// </summary>
        STATUS_SUCCESS = 0x00000000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OSVERSIONINFOEX
    {
        // The OSVersionInfoSize field must be set to Marshal.SizeOf(typeof(OSVERSIONINFOEX))
        public int OSVersionInfoSize;
        public int MajorVersion;
        public int MinorVersion;
        public int BuildNumber;
        public int PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string CSDVersion;
        public ushort ServicePackMajor;
        public ushort ServicePackMinor;
        public short SuiteMask;
        public byte ProductType;
        public byte Reserved;
    }
}
