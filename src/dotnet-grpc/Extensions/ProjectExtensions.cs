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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Options;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Extensions
{
    internal static class ProjectExtensions
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        public static void EnsureGrpcPackagesInProjectAsync(this Project project)
        {
            AddPackageReferenceAsync(project, "Google.Protobuf", "3.7.0");
            AddPackageReferenceAsync(project, "Grpc.AspNetCore.Server", "0.1.20-pre1");
            AddPackageReferenceAsync(project, "Grpc.Tools", "1.21.0-pre1", privateAssets: true);
        }

        private static void AddPackageReferenceAsync(this Project project, string packageName, string packageVersion, bool privateAssets = false)
        {
            var packageReference = project.GetItems("PackageReference").SingleOrDefault(i => i.UnevaluatedInclude == packageName);

            if (packageReference == null)
            {
                packageReference = project.AddItem("PackageReference", packageName).Single();
                packageReference.Xml.AddMetadata("Version", packageVersion, expressAsAttribute: true);

                if (privateAssets)
                {
                    packageReference.Xml.AddMetadata("PrivateAssets", "true", expressAsAttribute: true);
                }
            }
        }

        public static void AddProtobufReference(this Project project, Services services, string additionalImportDirs, Access access, string file, string url)
        {
            if (!project.GetItems("Protobuf").Any(i => i.UnevaluatedInclude == file))
            {
                // TODO (johluo): Log warning if the file doesn't have the .proto extension. Technically, this is not a requirement but it could lead to errors during compilation.

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

        public static Project ResolveProject(FileInfo? project)
        {
            if (project != null)
            {
                return new Project(project.FullName);
            }

            var currentDirectory = Directory.GetCurrentDirectory();
            var projectFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");

            if (projectFiles.Length == 0)
            {
                throw new InvalidOperationException($"Could not find any project in `{currentDirectory}`. Please specify a project explicitly.");
            }

            if (projectFiles.Length > 1)
            {
                throw new Exception($"Found more than one project in `{currentDirectory}`. Please specify which one to use.");
            }

            return new Project(projectFiles[0]);
        }

        public static string[] ExpandReferences(this Project project, string[] references)
        {
            var expandedReferences = new List<string>();

            foreach (var reference in references)
            {
                if (reference.StartsWith("http") && Uri.TryCreate(reference, UriKind.Absolute, out var _))
                {
                    expandedReferences.Add(reference);
                    continue;
                }

                if (Path.IsPathRooted(reference))
                {
                    var directoryToSearch = Path.GetPathRoot(reference);
                    var searchPattern = reference.Substring(directoryToSearch.Length);

                    expandedReferences.AddRange(Directory.GetFiles(directoryToSearch, searchPattern));
                    continue;
                }

                expandedReferences.AddRange(Directory.GetFiles(project.DirectoryPath, reference));
            }

            return expandedReferences.ToArray();
        }

        public static void RemoveProtobufReference(this Project project, ProjectItem protobufRef, bool removeFile)
        {
            project.RemoveItem(protobufRef);

            if (removeFile)
            {
                File.Delete(Path.IsPathRooted(protobufRef.UnevaluatedInclude) ? protobufRef.UnevaluatedInclude : Path.Combine(project.DirectoryPath, protobufRef.UnevaluatedInclude));
            }
        }

        public static bool IsUrl(string reference)
        {
            return reference.StartsWith("http") && Uri.TryCreate(reference, UriKind.Absolute, out var _);
        }

        public static async Task DownloadFileAsync(this Project project, string url, string destination, bool overwrite = false)
        {
            if (!Path.IsPathRooted(destination))
            {
                destination = Path.Combine(project.DirectoryPath, destination);
            }

            if (!overwrite && File.Exists(destination))
            {
                return;
            }

            using (var stream = await HttpClient.GetStreamAsync(url))
            {

                var desitnationDirectory = Path.GetDirectoryName(destination);
                if (!Directory.Exists(desitnationDirectory))
                {
                    Directory.CreateDirectory(desitnationDirectory);
                }

                using (var fileStream = File.Open(destination, FileMode.Create, FileAccess.Write))
                {
                    await stream.CopyToAsync(fileStream);
                }
            }
        }
    }
}
