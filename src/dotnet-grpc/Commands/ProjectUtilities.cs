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

using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using NuGet.CommandLine.XPlat;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Grpc.Dotnet.Cli.Commands
{
    internal static class ProjectUtilities
    {
        private static MSBuildAPIUtility MSBuild = new MSBuildAPIUtility(NullLogger.Instance);

        public static async Task<int> EnsureGrpcPackagesAsync(Project project)
        {
            var exitCode = await AddPackageReferenceAsync(project, "Google.Protobuf", "3.7.0");
            if (exitCode != 0)
            {
                return exitCode;
            }

            exitCode = await AddPackageReferenceAsync(project, "Grpc.AspNetCore.Server", "0.1.20-pre1");
            if (exitCode != 0)
            {
                return exitCode;
            }

            return await AddPackageReferenceAsync(project, "Grpc.Tools", "1.21.0-pre1", privateAssets: true);
        }

        private static async Task<int> AddPackageReferenceAsync(Project project, string packageName, string packageVersion, bool privateAssets = false)
        {
            var packageDependency = new PackageDependency(packageName, VersionRange.Parse(packageVersion));
            var packageRefArgs = new PackageReferenceArgs(project.FullPath, packageDependency, NullLogger.Instance)
            {
                NoRestore = true
            };

            var exitCode = await new AddPackageReferenceCommandRunner().ExecuteCommand(packageRefArgs, MSBuild);

            if (privateAssets)
            {
                project.Items.Single(i => i.ItemType == "PackageReference" && i.UnevaluatedInclude == packageName).SetMetadataValue("PrivateAssets", "true");
                project.Save();
            }

            return exitCode;
        }

    }
}
