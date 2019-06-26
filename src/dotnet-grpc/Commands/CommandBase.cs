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
using System.Diagnostics;
using System.Globalization;
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
        // static IDs for msbuild elements
        internal static readonly string PackageReferenceElement = "PackageReference";
        internal static readonly string VersionElement = "Version";
        internal static readonly string PrivateAssetsElement = "PrivateAssets";
        internal static readonly string ProtobufElement = "Protobuf";
        internal static readonly string GrpcServicesElement = "GrpcServices";
        internal static readonly string AccessElement = "Access";
        internal static readonly string AdditionalImportDirsElement = "AdditionalImportDirs";
        internal static readonly string SourceUrlElement = "SourceUrl";
        internal static readonly string LinkElement = "Link";
        internal static readonly string ProtosFolder = "Protos";
        internal static readonly string WebSDKProperty = "UsingMicrosoftNETSdkWeb";

        private readonly HttpClient _httpClient;

        public CommandBase(IConsole console, FileInfo? projectPath)
            : this(console, ResolveProject(projectPath), new HttpClient()) { }

        // Internal for testing
        internal CommandBase(IConsole console, Project project)
            : this(console, project, new HttpClient()) { }

        public CommandBase(IConsole console, HttpClient httpClient)
            : this(console, ResolveProject(null), httpClient) { }

        internal CommandBase(IConsole console, Project project, HttpClient httpClient)
        {
            Console = console;
            Project = project;
            _httpClient = httpClient;
        }

        internal IConsole Console { get; set; }
        internal Project Project { get; set; }

        public Services ResolveServices(Services services)
        {
            // Return the explicitly set services
            if (services != Services.Default)
            {
                return services;
            }

            // If UsingMicrosoftNETSdkWeb is true, generate Client and Server services
            if (Project.AllEvaluatedProperties.Any(p => string.Equals(WebSDKProperty, p.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals("true", p.UnevaluatedValue, StringComparison.OrdinalIgnoreCase)))
            {
                return Services.Both;
            }

            // If UsingMicrosoftNETSdkWeb is not true, genereate Client only
            return Services.Client;
        }

        public void EnsureNugetPackages(Services services)
        {
            Debug.Assert(services != Services.Default);

            foreach (var dependency in GetType().Assembly.GetCustomAttributes<GrpcDependencyAttribute>())
            {
                if (dependency.ApplicableServices.Split(';').Any(s => string.Equals(s, services.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    AddNugetPackage(dependency.Name, dependency.Version, dependency.PrivateAssets);
                }
            }
        }

        private void AddNugetPackage(string packageName, string packageVersion, string privateAssets)
        {
            var packageReference = Project.GetItems(PackageReferenceElement).SingleOrDefault(i => string.Equals(i.UnevaluatedInclude, packageName, StringComparison.OrdinalIgnoreCase));

            if (packageReference == null)
            {
                Console.Log(CoreStrings.LogAddPackageReference, packageName);

                packageReference = Project.AddItem(PackageReferenceElement, packageName).Single();
                packageReference.Xml.AddMetadata(VersionElement, packageVersion, expressAsAttribute: true);

                if (!string.Equals(privateAssets, "Default", StringComparison.OrdinalIgnoreCase))
                {
                    packageReference.Xml.AddMetadata(PrivateAssetsElement, privateAssets, expressAsAttribute: true);
                }
            }
        }

        public void AddProtobufReference(Services services, string additionalImportDirs, Access access, string file, string url)
        {
            var resolvedPath = Path.IsPathRooted(file) ? file : Path.Join(Project.DirectoryPath, file);
            if (!File.Exists(resolvedPath))
            {
                throw new CLIToolException(string.Format(CultureInfo.CurrentCulture, CoreStrings.ErrorReferenceDoesNotExist, file));
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

                // If file is outside of the project, display the file under Protos/ directory
                if (!Path.GetFullPath(resolvedPath).StartsWith(Project.DirectoryPath, StringComparison.OrdinalIgnoreCase))
                {
                    newItem.Xml.AddMetadata(LinkElement, Path.Combine(ProtosFolder, Path.GetFileName(file)!));
                }
            }
        }

        public static Project ResolveProject(FileInfo? project)
        {
            if (project != null)
            {
                if (!File.Exists(project.FullName))
                {
                    throw new CLIToolException(string.Format(CultureInfo.CurrentCulture, CoreStrings.ErrorProjectDoesNotExist, project.FullName));
                }

                return new Project(project.FullName);
            }

            var currentDirectory = Directory.GetCurrentDirectory();
            var projectFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");

            if (projectFiles.Length == 0)
            {
                throw new CLIToolException(string.Format(CultureInfo.CurrentCulture, CoreStrings.ErrorNoProjectFound, currentDirectory));
            }

            if (projectFiles.Length > 1)
            {
                throw new CLIToolException(string.Format(CultureInfo.CurrentCulture, CoreStrings.ErrorMoreThanOneProjectFound, currentDirectory));
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

                // The GetFullPath calls are used to resolve paths which may be equivalent but not identitcal
                // For example: Proto/a.proto and Proto/../Proto/a.proto
                var localItem = protobufItems.SingleOrDefault(p => string.Equals(Path.GetFullPath(p.UnevaluatedInclude), Path.GetFullPath(reference), StringComparison.OrdinalIgnoreCase));

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
                    var directoryToSearch = Path.GetPathRoot(reference)!;
                    var searchPattern = reference.Substring(directoryToSearch.Length);

                    expandedReferences.AddRange(Directory.GetFiles(directoryToSearch, searchPattern));
                    continue;
                }

                if (Directory.Exists(Path.Combine(Project.DirectoryPath, Path.GetDirectoryName(reference)!)))
                {
                    expandedReferences.AddRange(
                        Directory.GetFiles(Project.DirectoryPath, reference)
                            // The reference is relative to the project directory but GetFiles returns the full path.
                            // Remove the project directory portion of the path so relative references are maintained.
                            .Select(r => r.Replace(Project.DirectoryPath + Path.DirectorySeparatorChar, string.Empty)));
                }
            }

            return expandedReferences.ToArray();
        }

        public static bool IsUrl(string reference)
        {
            return Uri.TryCreate(reference, UriKind.Absolute, out var _) && reference.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        }

        public async Task DownloadFileAsync(string url, string destination, bool dryRun = false)
        {
            // The user must not specify a directory
            if (Path.EndsInDirectorySeparator(destination))
            {
                throw new CLIToolException(string.Format(CoreStrings.ErrorOutputMustBeFilePath, destination));
            }

            var resolveDestination = Path.IsPathRooted(destination) ? destination : Path.Combine(Project.DirectoryPath, destination);
            var contentNotModified = true;

            if (!File.Exists(resolveDestination))
            {
                // The user must not specify an existing directory
                if (Directory.Exists(resolveDestination))
                {
                    throw new CLIToolException(string.Format(CoreStrings.ErrorOutputMustBeFilePath, destination));
                }

                // The destination file doesn't exist so content is modified.
                contentNotModified = false;

                var destinationDirectory = Path.GetDirectoryName(resolveDestination);
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }
            }
            else
            {
                try
                {
                    using (var stream = await _httpClient.GetStreamAsync(url))
                    using (var fileStream = File.OpenRead(resolveDestination))
                    {
                        contentNotModified = IsStreamContentIdentical(stream, fileStream);
                    }
                }
                catch (HttpRequestException e)
                {
                    throw new CLIToolException(e.Message);
                }
            }

            if (contentNotModified)
            {
                Console.Log(CoreStrings.LogSkipDownload, destination, url);
                return;
            }

            Console.Log(CoreStrings.LogDownload, destination, url);
            if (!dryRun)
            {
                try
                {
                    using (var stream = await _httpClient.GetStreamAsync(url))
                    using (var fileStream = File.Open(resolveDestination, FileMode.Create, FileAccess.Write))
                    {
                        await stream.CopyToAsync(fileStream);
                        await fileStream.FlushAsync();
                    }
                }
                catch (HttpRequestException e)
                {
                    throw new CLIToolException(e.Message);
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
