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
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Options;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands
{
    internal class HandlerBase
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        internal IConsole? Console { get; set; }
        internal Project? Project { get; set; }

        public void EnsureGrpcPackagesInProjectAsync()
        {
            AddPackageReferenceAsync("Google.Protobuf", "3.7.0");
            AddPackageReferenceAsync("Grpc.AspNetCore.Server", "0.1.20-pre1");
            AddPackageReferenceAsync("Grpc.Tools", "1.21.0-pre1", privateAssets: true);
        }

        private void AddPackageReferenceAsync(string packageName, string packageVersion, bool privateAssets = false)
        {
            if (Project == null)
            {
                throw new InvalidOperationException("Internal error: Project not set.");
            }

            var packageReference = Project.GetItems("PackageReference").SingleOrDefault(i => i.UnevaluatedInclude == packageName);

            if (packageReference == null)
            {
                packageReference = Project.AddItem("PackageReference", packageName).Single();
                packageReference.Xml.AddMetadata("Version", packageVersion, expressAsAttribute: true);

                if (privateAssets)
                {
                    packageReference.Xml.AddMetadata("PrivateAssets", "true", expressAsAttribute: true);
                }
            }
        }

        public void AddProtobufReference(Services services, string additionalImportDirs, Access access, string file, string url)
        {
            if (Project == null)
            {
                throw new InvalidOperationException("Internal error: Project not set.");
            }

            if (!Project.GetItems("Protobuf").Any(i => i.UnevaluatedInclude == file))
            {
                // TODO (johluo): Log warning if the file doesn't have the .proto extension. Technically, this is not a requirement but it could lead to errors during compilation.

                var newItem = Project.AddItem("Protobuf", file).Single();

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

        public void ResolveProject(FileInfo? project)
        {
            if (project != null)
            {
                Project = new Project(project.FullName);
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

            Project = new Project(projectFiles[0]);
        }

        public string[] ExpandReferences(string[] references)
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

                expandedReferences.AddRange(Directory.GetFiles(Project!.DirectoryPath, reference));
            }

            return expandedReferences.ToArray();
        }

        public void RemoveProtobufReference(ProjectItem protobufRef, bool removeFile)
        {
            if (Project == null)
            {
                throw new InvalidOperationException("Internal error: Project not set.");
            }

            Project.RemoveItem(protobufRef);

            if (removeFile)
            {
                File.Delete(Path.IsPathRooted(protobufRef.UnevaluatedInclude) ? protobufRef.UnevaluatedInclude : Path.Combine(Project.DirectoryPath, protobufRef.UnevaluatedInclude));
            }
        }

        public static bool IsUrl(string reference)
        {
            return reference.StartsWith("http") && Uri.TryCreate(reference, UriKind.Absolute, out var _);
        }

        public async Task DownloadFileAsync(string url, string destination, bool overwrite = false, bool dryRun = false)
        {
            if (Project == null)
            {
                throw new InvalidOperationException("Internal error: Project not set.");
            }

            if (Console == null)
            {
                throw new InvalidOperationException("Internal error: Console not set.");
            }

            if (!Path.IsPathRooted(destination))
            {
                destination = Path.Combine(Project.DirectoryPath, destination);
            }

            if (!overwrite && File.Exists(destination))
            {
                return;
            }

            var contentNotModified = true;

            if (!File.Exists(destination))
            {
                // The desitnation file doesn't exist so refresh is needed.
                contentNotModified = false;

                var destinationDirectory = Path.GetDirectoryName(destination);
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }
            }
            else
            {
                using (var stream = await HttpClient.GetStreamAsync(url))
                using (var fileStream = File.OpenRead(destination))
                {
                    contentNotModified = IsStreamContentIdentical(stream, fileStream);
                }
            }

            if (contentNotModified)
            {
                Console.Out.WriteLine($"Content of {destination} is identical to the content at {url}, skipping.");
                return;
            }

            Console.Out.WriteLine($"Content of {destination} is different from the content at {url}, updating with remote content.");

            if (!dryRun)
            {
                using (var stream = await HttpClient.GetStreamAsync(url))
                using (var fileStream = File.Open(destination, FileMode.Create, FileAccess.Write))
                {
                    await stream.CopyToAsync(fileStream);
                    fileStream.Flush();
                }
            }
        }

        private static bool IsStreamContentIdentical(Stream remote, Stream local)
        {
            var remoteHash = GetHash(remote);
            var localHash = GetHash(local);

            if (remoteHash.Length != localHash.Length)
            {
                return false;
            }

            for (var i = 0; i < remoteHash.Length; i++)
            {
                if (remoteHash[i] != localHash[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static byte[] GetHash(Stream stream)
        {
            SHA256 algorithm;
            try
            {
                algorithm = SHA256.Create();
            }
            catch (TargetInvocationException)
            {
                // SHA256.Create is documented to throw this exception on FIPS-compliant machines. See
                // https://msdn.microsoft.com/en-us/library/z08hz7ad Fall back to a FIPS-compliant SHA256 algorithm.
                algorithm = new SHA256CryptoServiceProvider();
            }

            using (algorithm)
            {
                return algorithm.ComputeHash(stream);
            }
        }
    }
}
