# Release Process for grpc-dotnet

This document describes the steps to create a new release of grpc-dotnet.
The release process is based on the release process used by the [grpc/grpc](https://github.com/grpc/grpc) repository.

Releases follow the [gRPC-wide release schedule](https://github.com/grpc/grpc/blob/master/doc/grpc_release_schedule.md).
There might be a slight delay if there are release blockers to resolve and/or we need to wait for the Grpc.Core.Api 
release that the new release depends on.

## Releasing a new version of grpc-dotnet (every 6 weeks)

- Before cutting the release branch
    - If needed, on the master branch update the `<GrpcPackageVersion>` dependency version in [dependencies.props](https://github.com/grpc/grpc-dotnet/blob/master/build/dependencies.props)
      to the latest pre-release of `Grpc.Core.Api` (that was just released as part of the grpc/grpc release process) 
    - Make sure that any patches/bugfixes from the last released branch have been applied to the master branch as well.

- Cut the release branch from master branch  (the branch format is `v2.25.x`, `v2.26.x`, ...).
  This release branch will be used for all subsequent pre-releases and patch releases within the same release cycle.

- On the release branch, replace the `-dev` suffix by `-pre1` under `<GrpcDotnetVersion>` version release number in [version.props](https://github.com/grpc/grpc-dotnet/blob/master/build/version.props)
  to prepare for building the pre-release nugets.
  Also check that the minor version number matches the branch name.

- Build the signed nuget packages and push them to nuget.org (internal process). **These are the pre-release packages.**

- Create a new release and tag in https://github.com/grpc/grpc-dotnet/releases (by creating the tag from the current release branch).
  Mark the release as "pre-release" in the github UI. Fill in the release notes.

- Wait for the stable release of `Grpc.Core.Api` (as scheduled by the release schedule), keep checking the issue tracker for problems with the new grpc-dotnet pre-release packages.
  If problems are discovered and they need to be fixed, the release manager might choose to release more pre-releases (`-pre2`, `-pre3`, ...) before deciding to release as stable.

- Once stable version of `Grpc.Core.Api` is pushed, remove `-pre1` suffix of the `<GrpcDotnetVersion>` version release number in [version.props](https://github.com/grpc/grpc-dotnet/blob/master/build/version.props) to prepare for the stable release.

- Build the signed nuget packages and push them to nuget.org (internal process). **These are the stable grpc-dotnet packages.**

- Create a new release and tag in https://github.com/grpc/grpc-dotnet/releases (by creating the tag from the current release branch).
  This is the stable release. Fill in the release notes.

### After the release

On the master branch, increase the version number to indicate start of a new release cycle (v2.25.0-dev become v2.26.0-dev)

## Process for patch releases

- Patch releases are created from an existing release branch by bumping the patch component of the version number
- When bugfixes are merged to a release branch, make sure they are synced into the master branch as well (otherwise the fixes might be missing in the next release cycle)
