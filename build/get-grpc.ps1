#!/usr/bin/env powershell

<#
.PARAMETER Upgrade
Upgrade the version of gRPC packages to be downloaded to the latest on https://packages.grpc.io/
.NOTES
This function will create a file grpc-lock.txt. This lock file can be committed to source, but does not have to be.
When the lock file is not present, the script will create one using latest available version from https://packages.grpc.io/.
#>

param (
    [switch]$Upgrade = $false
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

# Variables

$WorkingDir = $PSScriptRoot
$TempDir = Join-Path $WorkingDir 'obj'
$VersionUrl = 'https://raw.githubusercontent.com/grpc/grpc/master/src/csharp/Grpc.Core/Version.csproj.include'
$BuildsUrl = 'https://packages.grpc.io/'
$VersionPath = Join-Path $TempDir 'version.xml'
$BuildsPath = Join-Path $TempDir 'builds.xml'
$GrpcLockPath = Join-Path $WorkingDir 'grpc-lock.txt'
$FeedPath = Join-Path $WorkingDir 'feed'
$DependenciesPath = Join-Path $WorkingDir 'dependencies.props'
$Packages = "Grpc.Core", "Grpc.Core.Api", "Grpc.Tools", "Grpc.Reflection", "Grpc.Auth"

# Functions

function Ensure-Dir([string]$path) {
    if (!(Test-Path $path -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }
}

# Main

# This speeds up Invoke-WebRequest
$ProgressPreference = "SilentlyContinue"

if ($Upgrade -or !(Test-Path $GrpcLockPath))
{
    # Download manifests
    Ensure-Dir $TempDir
    Write-Host "Downloading manifest: $VersionUrl => $VersionPath"
    Invoke-WebRequest -Uri $VersionUrl -OutFile $VersionPath
    Write-Host "Downloading manifest: $BuildsUrl => $BuildsPath"
    Invoke-WebRequest -Uri $BuildsUrl -OutFile $BuildsPath

    # Retrieve package version
    [xml]$VersionXML = Get-Content $VersionPath
    $PackageVersion = $VersionXML.Project.PropertyGroup.GrpcCsharpVersion
    Write-Host "Latest package version: $PackageVersion"

    # Retrieve nupkg path
    [xml]$BuildsXML = Get-Content $BuildsPath
    $NuGetPath = $BuildsXML.packages.builds.ChildNodes.Item(0).path
    $NuGetPath = $NuGetPath.TrimEnd('index.xml')

    # Write to lock file
    Set-Content -Path $GrpcLockPath -Value "$PackageVersion $NuGetPath"

    # Update dependencies
    $DependenciesXML = New-Object xml
    $DependenciesXML.PreserveWhitespace = $true
    $DependenciesXML.Load($DependenciesPath)

    foreach ($Package in $Packages) {
        # Update dependencies.props
        $PackagePropertyName = "$($Package.Replace('.', ''))PackageVersion"
        $DependenciesXML.SelectSingleNode("/Project/PropertyGroup/$PackagePropertyName").InnerText = $PackageVersion
    }

    $DependenciesXML.Save($DependenciesPath)
}

# Read from lock file
$GrpcLockContents = (Get-Content -Path $GrpcLockPath) -split ' '
$PackageVersion = $GrpcLockContents[0]
$NuGetPath = $GrpcLockContents[1]

Ensure-Dir $FeedPath
foreach ($Package in $Packages) {
    # Download packages
    $PackageName = "$Package.$PackageVersion.nupkg"
    $PackagePath = Join-Path $FeedPath $PackageName
    $PackageUrl = "$($BuildsUrl)$($NuGetPath)csharp/$($PackageName)"
    Write-Host "Downloading $PackageUrl"
    Invoke-WebRequest -Uri $PackageUrl -OutFile $PackagePath
}
