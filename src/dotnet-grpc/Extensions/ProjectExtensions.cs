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

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Options;
using Microsoft.Build.Evaluation;
using NuGet.CommandLine.XPlat;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Grpc.Dotnet.Cli.Extensions
{
    internal static class ProjectExtensions
    {
        private static MSBuildAPIUtility MSBuild = new MSBuildAPIUtility(NullLogger.Instance);

        public static async Task<int> EnsureGrpcPackagesAsync(this Project project)
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

        private static async Task<int> AddPackageReferenceAsync(this Project project, string packageName, string packageVersion, bool privateAssets = false)
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

        public static void AddProtobufReference(this Project project, Services services, string additionalImportDirs, Access access, string file, string url)
        {
            if (!project.Items.Any(i => i.ItemType == "Protobuf" && i.UnevaluatedInclude == file))
            {
                var newItem = project.AddItem("Protobuf", file).Single();

                if (services != Services.Both)
                {
                    newItem.Xml.AddMetadata("GrpcServices", services.ToString(), expressAsAttribute: true);
                }

                if (access != Access.Public)
                {
                    newItem.Xml.AddMetadata("Access", access.ToString(), expressAsAttribute: true);
                }

                if (!string.IsNullOrEmpty(additionalImportDirs))
                {
                    newItem.Xml.AddMetadata("AdditionalImportDirs", additionalImportDirs, expressAsAttribute: true);
                }

                if (!string.IsNullOrEmpty(url))
                {
                    newItem.Xml.AddMetadata("SourceURL", url);
                }
            }
        }

        public static FileInfo? ResolveProjectPath()
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var projectFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");

            if (projectFiles.Length == 0)
            {
                Console.WriteLine($"Could not find any project in `{currentDirectory}`. Please specify a project explicitly.");
                return null;
            }

            if (projectFiles.Length > 1)
            {
                Console.WriteLine($"Found more than one project in `{currentDirectory}`. Please specify which one to use.");
                return null;
            }

            return new FileInfo(projectFiles[0]);
        }
    }
}
