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

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Grpc.Net.Client.Internal;

internal static class UserAgentGenerator
{
    /// <summary>
    /// Generates a user agent string to be transported in headers.
    /// <example>
    ///   grpc-dotnet/2.41.0-dev (.NET 6.0.0-preview.7.21377.19; CLR 6.0.0; net6.0; osx; x64)
    ///   grpc-dotnet/2.41.0-dev (Mono 6.12.0.140; CLR 4.0.30319; netstandard2.0; osx; x64)
    ///   grpc-dotnet/2.41.0-dev (.NET 6.0.0-rc.1.21380.1; CLR 6.0.0; net6.0; linux; arm64)
    ///   grpc-dotnet/2.41.0-dev (.NET 5.0.8; CLR 5.0.8; net5.0; linux; arm64)
    ///   grpc-dotnet/2.41.0-dev (.NET Core; CLR 3.1.4; netstandard2.1; linux; arm64)
    ///   grpc-dotnet/2.41.0-dev (.NET Framework; CLR 4.0.30319.42000; netstandard2.0; windows; x86)
    ///   grpc-dotnet/2.41.0-dev (.NET 6.0.0-rc.1.21380.1; CLR 6.0.0; net6.0; windows; x64)
    /// </example>
    /// </summary>
    internal static string GetUserAgentString()
    {
        var assembly = typeof(GrpcProtocolConstants).Assembly;

        var assemblyVersion = assembly
            .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?
            .InformationalVersion;

        var frameworkName = assembly
            .GetCustomAttributes<TargetFrameworkAttribute>()
            .FirstOrDefault()?
            .FrameworkName;

        return GetUserAgentString(
            processArch: RuntimeInformation.ProcessArchitecture,
            clrVersion: Environment.Version,
            assemblyVersion: assemblyVersion,
            // RuntimeInformation.FrameworkDescription is only supported for
            // .NET Framework 4.7.1 and above. If targeting net461 or earlier,
            // the framework description will need to be resolved manually
            // using reflection.
            runtimeInformation: RuntimeInformation.FrameworkDescription,
            frameworkName: frameworkName,
            operatingSystem: GetOS());
    }

    // Factored out for testing
    internal static string GetUserAgentString(Architecture processArch, Version? clrVersion, string? assemblyVersion, string? runtimeInformation, string? frameworkName, string? operatingSystem)
    {
        var userAgent = "grpc-dotnet";

        // /2.41.0-dev
        userAgent += $"{GetGrpcDotNetVersion(assemblyVersion)} ";
        // (.NET 5.0.7;
        userAgent += $"({GetFrameworkDescription(runtimeInformation)}";
        // CLR 5.0.0;
        userAgent += GetClrVersion(clrVersion);
        // net6.0;
        userAgent += GetFrameworkName(frameworkName);
        // windows  
        userAgent += $"{operatingSystem}";
        // x64)
        userAgent += $"{GetProcessArch(processArch)})";

        static string GetClrVersion(Version? clrVersion) => clrVersion != null ? $"CLR {clrVersion}; " : string.Empty;

        static string GetProcessArch(Architecture processArch) => processArch.ToString().ToLowerInvariant();

        return userAgent;
    }

    private static string GetGrpcDotNetVersion(string? assemblyVersion)
    {
        // Assembly file version attribute should always be present,
        // but in case it isn't then don't include version in user-agent.
        if (!string.IsNullOrEmpty(assemblyVersion))
        {
            // Strip the git hash if there is one
            int plusIndex = assemblyVersion!.IndexOf("+", StringComparison.Ordinal);
            if (plusIndex != -1)
            {
                assemblyVersion = assemblyVersion.Substring(0, plusIndex);
            }
            // /2.41.0-dev
            return $"/{assemblyVersion}";
        }

        return string.Empty;
    }

    private static string GetOS()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows; ";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "osx; ";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux; ";
        }
        else
        {
            return string.Empty;
        }
    }

    // Maps ".NETCoreApp,Version=v6.0;" to "net6.0"
    private static string GetFrameworkName(string? frameworkName)
    {
        if (string.IsNullOrEmpty(frameworkName))
        {
            return string.Empty;
        }

        var splitFramework = frameworkName!.Split(',');
#if !NETSTANDARD2_0 && !NET462
        var version = Version.Parse(splitFramework[1].AsSpan("Version=v".Length));
#else
        var version = Version.Parse(splitFramework[1].Substring("Version=v".Length));
#endif
        var name = splitFramework[0] switch
        {
            ".NETCoreApp" when version.Major < 5 => $"netcoreapp{version.ToString(2)}",
            ".NETCoreApp" when version.Major >= 5 => $"net{version.ToString(2)}",
#if !NETSTANDARD2_0 && !NET462
            ".NETFramework" => $"net{version.ToString().Replace(".", string.Empty, StringComparison.OrdinalIgnoreCase)}",
#else
            ".NETFramework" => $"net{version.ToString().Replace(".", string.Empty)}",
#endif
            ".NETStandard" => $"netstandard{version.ToString(2)}",
            _ => frameworkName
        };

        return $"{name}; ";
    }

    private static string GetFrameworkDescription(string? frameworkDescription)
    {
        if (!string.IsNullOrEmpty(frameworkDescription))
        {
            // FrameworkDescription is typically represented as {FrameworkName} {VersionString}
            // where VersionString is optional and variable.
            var splitFrameworkDescription = frameworkDescription!.Split(' ');
            if (splitFrameworkDescription.Length == 1)
            {
                return $"{splitFrameworkDescription[0]}; ";
            }
            return $"{splitFrameworkDescription[0]} {splitFrameworkDescription[1]}; ";
        }
        return string.Empty;
    }
}
