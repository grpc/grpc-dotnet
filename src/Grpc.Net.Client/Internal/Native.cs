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
internal static class Native
{
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
    [DllImport("ntdll.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern NTSTATUS RtlGetVersion(ref OSVERSIONINFOEX versionInfo);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentApplicationUserModelId(ref uint applicationUserModelIdLength, byte[] applicationUserModelId);
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

    internal static void DetectWindowsVersion(out Version version, out bool isWindowsServer)
    {
        // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-osversioninfoexa
        const byte VER_NT_WORKSTATION = 1;

        var osVersionInfo = new OSVERSIONINFOEX { OSVersionInfoSize = Marshal.SizeOf<OSVERSIONINFOEX>() };

        if (RtlGetVersion(ref osVersionInfo) != NTSTATUS.STATUS_SUCCESS)
        {
            throw new InvalidOperationException($"Failed to call internal {nameof(RtlGetVersion)}.");
        }

        version = new Version(osVersionInfo.MajorVersion, osVersionInfo.MinorVersion, osVersionInfo.BuildNumber, 0);
        isWindowsServer = osVersionInfo.ProductType != VER_NT_WORKSTATION;
    }

    internal static bool IsUwp(string frameworkDescription, Version version)
    {
        if (frameworkDescription.StartsWith(".NET Native", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // From https://github.com/dotnet/runtime/blob/d752f9a19f2d4bc4559e0e303e9374e4042a916e/src/libraries/Common/tests/TestUtilities/System/PlatformDetection.Windows.cs#L190
        const int Windows8Build = 9200;
        if (version.Build < Windows8Build)
        {
            return false;
        }
        else
        {
            try
            {
                var bufferSize = 0U;
                var result = GetCurrentApplicationUserModelId(ref bufferSize, Array.Empty<byte>());
                switch (result)
                {
                    case 15703: // APPMODEL_ERROR_NO_APPLICATION
                        return false;
                    case 0:     // ERROR_SUCCESS
                    case 122:   // ERROR_INSUFFICIENT_BUFFER
                                // Success is actually insufficient buffer as we're really only looking for
                                // not NO_APPLICATION and we're not actually giving a buffer here. The
                                // API will always return NO_APPLICATION if we're not running under a
                                // WinRT process, no matter what size the buffer is.
                        return true;
                    default:
                        throw new InvalidOperationException($"Failed to get AppModelId, result was {result}.");
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Wine compatibility layers such as Steam Deck/Steam OS Proton and the Apple Game Porting Toolkit
                // return Windows 8 or later as the OS version, but does not implement the GetCurrentApplicationUserModelId API.
                return false;
            }
        }
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
