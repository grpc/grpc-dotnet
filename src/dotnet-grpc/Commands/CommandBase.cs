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

using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Grpc.Dotnet.Cli.Internal;
using Grpc.Dotnet.Cli.Options;
using Grpc.Dotnet.Cli.Properties;
using Microsoft.Build.Evaluation;

namespace Grpc.Dotnet.Cli.Commands;

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
    internal static readonly string UsingWebSDKPropertyName = "UsingMicrosoftNETSdkWeb";
    internal static readonly string PackageVersionUrl = "https://go.microsoft.com/fwlink/?linkid=2099561";

    private readonly HttpClient _httpClient;

    public CommandBase(IConsole console, string? projectPath, HttpClient client)
        : this(console, ResolveProject(projectPath), client) { }

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
    private bool IsUsingWebSdk => Project.AllEvaluatedProperties.Any(p => string.Equals(UsingWebSDKPropertyName, p.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals("true", p.UnevaluatedValue, StringComparison.OrdinalIgnoreCase));

    public Services ResolveServices(Services services)
    {
        // Return the explicitly set services
        if (services != Services.Default)
        {
            return services;
        }

        // If UsingMicrosoftNETSdkWeb is true, generate Client and Server services
        if (IsUsingWebSdk)
        {
            return Services.Both;
        }

        // If UsingMicrosoftNETSdkWeb is not true, genereate Client only
        return Services.Client;
    }

    public async Task EnsureNugetPackagesAsync(Services services)
    {
        var packageVersions = await ResolvePackageVersions();

        Debug.Assert(services != Services.Default);

        foreach (var dependency in GetType().Assembly.GetCustomAttributes<GrpcDependencyAttribute>())
        {
            // Check if the dependency is applicable for this service type
            if (dependency.ApplicableServices.Split(';').Any(s => string.Equals(s, services.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                // Check if the dependency is applicable to this SDK type
                if (dependency.ApplicableToWeb == null || string.Equals(dependency.ApplicableToWeb, IsUsingWebSdk.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                {
                    // Use the version specified from the remote file before falling back the packaged versions
                    var packageVersion = packageVersions?.GetValueOrDefault(dependency.Name) ?? dependency.Version;
                    AddNugetPackage(dependency.Name, packageVersion, dependency.PrivateAssets);
                }
            }
        }
    }

    private async Task<Dictionary<string, string>?> ResolvePackageVersions()
    {
        /* Example Json content
         {
          "Version" : "1.0",
          "Packages"  :  {
            "Microsoft.Azure.SignalR": "1.1.0-preview1-10442",
            "Grpc.AspNetCore.Server": "0.1.22-pre2",
            "Grpc.Net.ClientFactory": "0.1.22-pre2",
            "Google.Protobuf": "3.8.0",
            "Grpc.Tools": "1.22.0",
            "NSwag.ApiDescription.Client": "13.0.3",
            "Microsoft.Extensions.ApiDescription.Client": "0.3.0-preview7.19365.7",
            "Newtonsoft.Json": "12.0.2"
          }
        }*/
        try
        {
            await using var packageVersionStream = await _httpClient.GetStreamAsync(PackageVersionUrl);
            using var packageVersionDocument = await JsonDocument.ParseAsync(packageVersionStream);
            var packageVersionsElement = packageVersionDocument.RootElement.GetProperty("Packages");
            var packageVersionsDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var packageVersion in packageVersionsElement.EnumerateObject())
            {
                packageVersionsDictionary[packageVersion.Name] = packageVersion.Value.GetString()!;
            }

            return packageVersionsDictionary;
        }
        catch
        {
            // TODO (johluo): Consider logging a message indicating what went wrong and actions, if any, to be taken to resolve possible issues.
            // Currently not logging anything since the fwlink is not published yet.
            return null;
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

    public void AddProtobufReference(Services services, string? additionalImportDirs, Access access, string file, string url)
    {
        var resolvedPath = Path.IsPathRooted(file) ? file : Path.Join(Project.DirectoryPath, file);
        if (!File.Exists(resolvedPath))
        {
            throw new CLIToolException(string.Format(CultureInfo.CurrentCulture, CoreStrings.ErrorReferenceDoesNotExist, file));
        }

        var normalizedFile = NormalizePath(file);
        
        var normalizedAdditionalImportDirs = string.Empty;

        if (!string.IsNullOrWhiteSpace(additionalImportDirs))
        {
            normalizedAdditionalImportDirs = string.Join(';', additionalImportDirs.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(NormalizePath));
        }

        if (!Project.GetItems(ProtobufElement).Any(i => string.Equals(NormalizePath(i.UnevaluatedInclude), normalizedFile, StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.Equals(Path.GetExtension(file), ".proto", StringComparison.OrdinalIgnoreCase))
            {
                Console.LogWarning(CoreStrings.LogWarningReferenceNotProto, file);
            }

            var newItem = Project.AddItem(ProtobufElement, normalizedFile).Single();

            if (services != Services.Both)
            {
                newItem.Xml.AddMetadata(GrpcServicesElement, services.ToString(), expressAsAttribute: true);
            }

            if (access != Access.Public)
            {
                newItem.Xml.AddMetadata(AccessElement, access.ToString(), expressAsAttribute: true);
            }

            if (!string.IsNullOrEmpty(normalizedAdditionalImportDirs))
            {
                newItem.Xml.AddMetadata(AdditionalImportDirsElement, normalizedAdditionalImportDirs, expressAsAttribute: true);
            }

            if (!string.IsNullOrEmpty(url))
            {
                newItem.Xml.AddMetadata(SourceUrlElement, url);
            }

            // If file is outside of the project, display the file under Protos/ directory
            if (!Path.GetFullPath(resolvedPath).StartsWith(Project.DirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
                newItem.Xml.AddMetadata(LinkElement, $"{ProtosFolder}\\{Path.GetFileName(file)}", expressAsAttribute: true);
            }
        }
    }

    public static Project ResolveProject(string? project)
    {
        if (project != null)
        {
            if (File.Exists(project))
            {
                return new Project(project);
            }
            if (Directory.Exists(project))
            {
                return LoadFromDirectoryPath(project);
            }

            throw new CLIToolException(string.Format(CultureInfo.CurrentCulture, CoreStrings.ErrorProjectDoesNotExist, project));
        }

        var currentDirectory = Directory.GetCurrentDirectory();
        return LoadFromDirectoryPath(currentDirectory);
    }

    private static Project LoadFromDirectoryPath(string currentDirectory)
    {
        var projectFiles = Directory.GetFiles(currentDirectory, "*.csproj");

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

                var resolvedFiles = Directory.GetFiles(directoryToSearch, searchPattern);

                if (resolvedFiles.Length == 0)
                {
                    Console.LogWarning(CoreStrings.LogWarningNoReferenceResolved, reference);
                }

                expandedReferences.AddRange(resolvedFiles);
                continue;
            }

            if (Directory.Exists(Path.Combine(Project.DirectoryPath, Path.GetDirectoryName(reference)!)))
            {
                var resolvedFiles = Directory.GetFiles(Project.DirectoryPath, reference);

                if (resolvedFiles.Length == 0)
                {
                    Console.LogWarning(CoreStrings.LogWarningNoReferenceResolved, reference);
                }

                expandedReferences.AddRange(
                    // The reference is relative to the project directory but GetFiles returns the full path.
                    // Remove the project directory portion of the path so relative references are maintained.
                    resolvedFiles.Select(r => r.Replace(Project.DirectoryPath + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)));
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
            throw new CLIToolException(string.Format(CultureInfo.InvariantCulture, CoreStrings.ErrorOutputMustBeFilePath, destination));
        }

        var resolveDestination = Path.IsPathRooted(destination) ? destination : Path.Combine(Project.DirectoryPath, destination);
        var contentNotModified = true;

        if (!File.Exists(resolveDestination))
        {
            // The user must not specify an existing directory
            if (Directory.Exists(resolveDestination))
            {
                throw new CLIToolException(string.Format(CultureInfo.InvariantCulture, CoreStrings.ErrorOutputMustBeFilePath, destination));
            }

            // The destination file doesn't exist so content is modified.
            contentNotModified = false;

            var destinationDirectory = Path.GetDirectoryName(resolveDestination);
            if (!Directory.Exists(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory!);
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
        using (var algorithm = SHA256.Create())
        {
            return algorithm.ComputeHash(stream);
        }
    }

    private string NormalizePath(string path)
    {
        path = !Path.IsPathRooted(path)
            ? Path.GetRelativePath(Project.DirectoryPath, Path.GetFullPath(Path.Combine(Project.DirectoryPath, path)))
            : Path.GetFullPath(path);

        return path.Replace('/', '\\');
    }
}
