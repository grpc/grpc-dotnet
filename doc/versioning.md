# gRPC-dotnet package versioning policies

## Overview

This document covers versioning policies of the packages produced in this repo:

- Grpc.AspNetCore
- Grpc.AspNetCore.Server
- Grpc.AspNetCore.Server.ClientFactory
- Grpc.Net.Client
- Grpc.Net.ClientFactory
- Grpc.Net.Common
- dotnet-grpc

## Design considerations

### Major version number

The initial major version number was set to 2 to match the release of corresponding Grpc.Core packages. Going forward, the major version will be incremented when there's a need to ingest a major Grpc.Core or ASP.NET Core dependency for breaking changes. grpc-dotnet complies with the gRPC-wide [versioning policy](https://github.com/grpc/grpc/blob/master/doc/versioning.md) and the major version will only be incremented on rare occasions.

### Minor version number

The initial minor version number was set to 23 to match the release of corresponding Grpc.Core packages. Going forward, we will be releasing on the Grpc.Core [schedule](https://github.com/grpc/grpc/blob/master/doc/grpc_release_schedule.md) and will match the minor version number of the corresponding release.

### Patch version number

The patch number will increment for patches of existing major.minor release. These patches are built out of the v{major}.{minor}.x branches on GitHub, e.g. https://github.com/grpc/grpc-dotnet/tree/v2.23.x.

## Pre-release and nightly suffixes

The suffix -pre{x} will be added to pre-release packages released to nuget.org. These are non-stable packages intended for early ingestion of upcoming releases.
The suffix -dev is added to packages produced by nightly builds available from the NuGet feed https://grpc.jfrog.io/grpc/api/nuget/v3/grpc-nuget-dev.