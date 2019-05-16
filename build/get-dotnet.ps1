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
$InstallScriptUrl = 'https://dot.net/v1/dotnet-install.ps1'
$InstallScriptPath = Join-Path $TempDir 'dotnet-install.ps1'
$GlobalJsonPath = Join-Path $WorkingDir '..' | Join-Path -ChildPath 'global.json'
$DotnetInstallPath = Join-Path $WorkingDir '..' | Join-Path -ChildPath '.dotnet'

# Functions

function Ensure-Dir([string]$path) {
    if (!(Test-Path $path -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }
}

# Main

# Resolve SDK version
$GlobalJson = Get-Content -Raw $GlobalJsonPath | ConvertFrom-Json
$SDKVersion = $GlobalJson.sdk.version

# Download install script
Ensure-Dir $TempDir
Write-Host "Downloading install script: $InstallScriptUrl => $InstallScriptPath"
Invoke-WebRequest -Uri $InstallScriptUrl -OutFile $InstallScriptPath
&$InstallScriptPath -Version $SDKVersion -InstallDir $DotnetInstallPath