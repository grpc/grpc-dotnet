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
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Dotnet.Cli.Properties;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands
{
    internal class CommandBase
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        // static IDs for msbuild elements
        internal static readonly string PackageReferenceElement = "PackageReference";
        internal static readonly string VersionElement = "Version";
        internal static readonly string PrivateAssetsElement = "PrivateAssets";
        internal static readonly string ProtobufElement = "Protobuf";
        internal static readonly string GrpcServicesElement = "GrpcServices";
        internal static readonly string AccessElement = "Access";
        internal static readonly string AdditionalImportDirsElement = "AdditionalImportDirs";
        internal static readonly string SourceUrlElement = "SourceUrl";

        public CommandBase(IConsole console, FileInfo? projectPath)
            : this(console, ResolveProject(projectPath)) { }

        // Internal for testing
        internal CommandBase(IConsole console, Project project)
        {
            Console = console;
            Project = project;
        }

        internal IConsole Console { get; set; }
        internal Project Project { get; set; }

        public void EnsureNugetPackages()
        {
            // TODO (johluo): Tie these to dependencies.props
            AddNugetPackage("Google.Protobuf", "3.7.0");
            AddNugetPackage("Grpc.AspNetCore.Server", "0.1.20-pre1");
            AddNugetPackage("Grpc.Tools", "1.21.0-pre1", privateAssets: true);
        }

        private void AddNugetPackage(string packageName, string packageVersion, bool privateAssets = false)
        {
            var packageReference = Project.GetItems(PackageReferenceElement).SingleOrDefault(i => i.UnevaluatedInclude == packageName);

            if (packageReference == null)
            {
                Console.Log(CoreStrings.LogAddPackageReference, packageName);

                packageReference = Project.AddItem(PackageReferenceElement, packageName).Single();
                packageReference.Xml.AddMetadata(VersionElement, packageVersion, expressAsAttribute: true);

                if (privateAssets)
                {
                    packageReference.Xml.AddMetadata(PrivateAssetsElement, "All", expressAsAttribute: true);
                }
            }
        }

        public void AddProtobufReference(Services services, string additionalImportDirs, Access access, string file, string url)
        {
            if (!File.Exists(Path.IsPathRooted(file) ? file : Path.Join(Project.DirectoryPath, file)))
            {
                throw new CLIToolException($"The reference {file} does not exist.");
            }

            if (!Project.GetItems(ProtobufElement).Any(i => string.Equals(i.UnevaluatedInclude, file, StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.Equals(Path.GetExtension(file), ".proto", StringComparison.OrdinalIgnoreCase))
                {
                    Console.LogWarning(CoreStrings.LogWarningReferenceNotProto, file);
                }

                var newItem = Project.AddItem(ProtobufElement, file).Single();

                if (services != Services.Both)
                {
                    newItem.Xml.AddMetadata(GrpcServicesElement, services.ToString(), expressAsAttribute: true);
                }

                if (access != Access.Public)
                {
                    newItem.Xml.AddMetadata(AccessElement, access.ToString(), expressAsAttribute: true);
                }

                if (!string.IsNullOrEmpty(additionalImportDirs))
                {
                    newItem.Xml.AddMetadata(AdditionalImportDirsElement, additionalImportDirs, expressAsAttribute: true);
                }

                if (!string.IsNullOrEmpty(url))
                {
                    newItem.Xml.AddMetadata(SourceUrlElement, url);
                }
            }
        }

        public static Project ResolveProject(FileInfo? project)
        {
            if (project != null)
            {
                if (!File.Exists(project.FullName))
                {
                    throw new CLIToolException($"The project {project.FullName} does not exist.");
                }

                return new Project(project.FullName);
            }

            var currentDirectory = Directory.GetCurrentDirectory();
            var projectFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");

            if (projectFiles.Length == 0)
            {
                throw new CLIToolException($"Could not find any project in `{currentDirectory}`. Please specify a project explicitly.");
            }

            if (projectFiles.Length > 1)
            {
                throw new CLIToolException($"Found more than one project in `{currentDirectory}`. Please specify which one to use.");
            }

            return new Project(projectFiles[0]);
        }

        public IEnumerable<ProjectItem> ResolveReferences(string[] references)
        {
            if (references.Length == 0)
            {
                return Enumerable.Empty<ProjectItem>();
            }
            var resolvedReferences = new List<ProjectItem>();
            var protobufItems = Project.GetItems(ProtobufElement);

            foreach (var reference in GlobReferences(references))
            {
                if (IsUrl(reference))
                {
                    var remoteItem = protobufItems.SingleOrDefault(p => string.Equals(p.GetMetadataValue(SourceUrlElement), reference, StringComparison.OrdinalIgnoreCase));

                    if (remoteItem == null)
                    {
                        Console.LogWarning(CoreStrings.LogWarningCouldNotFindRemoteReference, reference);
                        continue;
                    }

                    resolvedReferences.Add(remoteItem);
                    continue;
                }

                var localItem = protobufItems.SingleOrDefault(p => string.Equals(p.UnevaluatedInclude, reference, StringComparison.OrdinalIgnoreCase));

                if (localItem == null)
                {
                    Console.LogWarning(CoreStrings.LogWarningCouldNotFindFileReference, reference);
                    continue;
                }

                resolvedReferences.Add(localItem);
            }

            return resolvedReferences;
        }

        internal string[] GlobReferences(string[] references)
        {
            var expandedReferences = new List<string>();

            foreach (var reference in references)
            {
                if (IsUrl(reference))
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

                try
                {
                    expandedReferences.AddRange(
                        Directory.GetFiles(Project.DirectoryPath, reference)
                            .Select(r => r.Replace(Project.DirectoryPath + Path.DirectorySeparatorChar, string.Empty)));
                }
                catch (DirectoryNotFoundException) { }
            }

            return expandedReferences.ToArray();
        }

        public void RemoveProtobufReference(ProjectItem protobufRef, bool removeFile)
        {
            Project.RemoveItem(protobufRef);

            if (removeFile)
            {
                File.Delete(Path.IsPathRooted(protobufRef.UnevaluatedInclude) ? protobufRef.UnevaluatedInclude : Path.Combine(Project.DirectoryPath, protobufRef.UnevaluatedInclude));
            }
        }

        public static bool IsUrl(string reference)
        {
            return Uri.TryCreate(reference, UriKind.Absolute, out var _) && reference.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        }

        public async Task DownloadFileAsync(string url, string destination, bool dryRun = false)
        {
            if (!Path.IsPathRooted(destination))
            {
                destination = Path.Combine(Project.DirectoryPath, destination);
            }

            var contentNotModified = true;

            if (!File.Exists(destination))
            {
                // The destination file doesn't exist so content is modified.
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
                Console.Log(CoreStrings.LogSkipDownload, destination, url);
                return;
            }

            if (!dryRun)
            {
                using (var stream = await HttpClient.GetStreamAsync(url))
                using (var fileStream = File.Open(destination, FileMode.Create, FileAccess.Write))
                {
                    await stream.CopyToAsync(fileStream);
                    await fileStream.FlushAsync();
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
