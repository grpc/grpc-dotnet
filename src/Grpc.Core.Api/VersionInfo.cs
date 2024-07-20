#region Copyright notice and license

// Copyright 2015 gRPC authors.
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

namespace Grpc.Core;

/// <summary>
/// Provides info about current version of gRPC.
/// See https://codingforsmarties.wordpress.com/2016/01/21/how-to-version-assemblies-destined-for-nuget/
/// for rationale about assembly versioning.
/// </summary>
public static class VersionInfo
{
    // TODO(jtattermusch): how to propagate versions from https://github.com/grpc/grpc-dotnet/blob/f238855fcb77cec01e87eb9cc08680c3420c132f/Directory.Build.props#L8-L10version into
    // public const string values?

    /// <summary>
    /// Current <c>AssemblyVersion</c> attribute of gRPC C# assemblies
    /// </summary>
    public const string CurrentAssemblyVersion = "2.0.0.0";

    /// <summary>
    /// Current <c>AssemblyFileVersion</c> of gRPC C# assemblies
    /// </summary>
    public const string CurrentAssemblyFileVersion = "2.66.0.0";

    /// <summary>
    /// Current version of gRPC C#
    /// </summary>
    public const string CurrentVersion = "2.66.0-dev";
}
