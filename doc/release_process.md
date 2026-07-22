# Release Process for grpc-dotnet

This document describes the steps to create a new release of grpc-dotnet.
The release process is based on the release process used by the [grpc/grpc](https://github.com/grpc/grpc) repository.

Releases follow the [gRPC-wide release schedule](https://github.com/grpc/grpc/blob/master/doc/grpc_release_schedule.md).
There might be a slight delay if there are release blockers to resolve and/or we need to wait for the Grpc.Core.Api 
release that the new release depends on.

## Releasing a new version of grpc-dotnet (every 6 weeks)

1. Before cutting the release branch:
   - If needed, on the master branch update the `Grpc.Tools` and `Grpc.Core` dependency versions in [Directory.Packages.props](https://github.com/grpc/grpc-dotnet/blob/master/Directory.Packages.props)
     to the latest applicable pre-release or stable version produced by the grpc/grpc release process.
   - Make sure that any patches or bug fixes from the last released branch have been applied to the master branch as well.
   - Confirm that all four version values listed below are set to the current development version on master. Do not assume that cutting a branch updates them.

2. Cut the release branch from the master branch (the branch format is `v2.25.x`, `v2.26.x`, ...).
   This release branch will be used for all subsequent pre-releases and patch releases within the same release cycle.

3. On the release branch, set **all four** version values for the pre-release. For example, when preparing `2.80.0-pre1`:

   | File | Value | Pre-release example |
   | --- | --- | --- |
   | [build/version.props](https://github.com/grpc/grpc-dotnet/blob/master/build/version.props) | `GrpcDotnetVersion` | `2.80.0-pre1` |
   | [build/version.props](https://github.com/grpc/grpc-dotnet/blob/master/build/version.props) | `GrpcDotnetAssemblyFileVersion` | `2.80.0.0` |
   | [src/Grpc.Core.Api/VersionInfo.cs](https://github.com/grpc/grpc-dotnet/blob/master/src/Grpc.Core.Api/VersionInfo.cs) | `CurrentVersion` | `2.80.0-pre1` |
   | [src/Grpc.Core.Api/VersionInfo.cs](https://github.com/grpc/grpc-dotnet/blob/master/src/Grpc.Core.Api/VersionInfo.cs) | `CurrentAssemblyFileVersion` | `2.80.0.0` |

   - The package and current versions include the pre-release suffix. The file versions are numeric and do not include it.
   - `GrpcDotnetAssemblyFileVersion` and `CurrentAssemblyFileVersion` must match and must not be lower than the file version of the previous stable release.
   - `GrpcDotnetAssemblyVersion` and `CurrentAssemblyVersion` remain `2.0.0.0`; verify that they still match.
   - Check that the release version matches the release branch name.

4. Build the signed NuGet packages (internal process). **These are the pre-release packages.**
   Before pushing them, inspect the package metadata and a DLL from each produced package that contains assemblies, and confirm that:
   - The NuGet package version and DLL `ProductVersion` match `GrpcDotnetVersion`.
   - The DLL `FileVersion` matches `GrpcDotnetAssemblyFileVersion`.
   - `Grpc.Core.Api.VersionInfo` reports the values set above.
   Push the packages to nuget.org only after these checks pass.

5. Create a new release and tag in https://github.com/grpc/grpc-dotnet/releases (by creating the tag from the current release branch).
   Mark the release as "pre-release" in the GitHub UI. Fill in the release notes.

6. Wait for the stable release of `Grpc.Tools` from the `grpc/grpc` repository (as scheduled by the release schedule), and keep checking the issue tracker for problems with the new grpc-dotnet pre-release packages.
   If problems are discovered and they need to be fixed, the release manager might choose to release more pre-releases (`-pre2`, `-pre3`, ...) before deciding to release as stable. For each additional pre-release, update both `GrpcDotnetVersion` and `CurrentVersion` to the new suffix and repeat the package checks in step 4.

7. **Make sure at least 7 days have elapsed since the last pre-release before proceeding with the stable release**, to give users enough time to test the pre-release NuGet packages and discover potential issues. This decreases the likelihood of pushing a bad stable release.

8. Once the stable version of `Grpc.Tools` is pushed, prepare the stable release:
   - Update the `Grpc.Tools` dependency in `Directory.Packages.props` if it does not already reference the stable version.
   - Remove the pre-release suffix from both `GrpcDotnetVersion` and `CurrentVersion`.
   - Verify again that `GrpcDotnetAssemblyFileVersion` and `CurrentAssemblyFileVersion` contain the numeric stable version and match each other. Do not leave either value at an older release.
   - Verify that `GrpcDotnetAssemblyVersion` and `CurrentAssemblyVersion` remain `2.0.0.0` and match each other.

9. Build the signed NuGet packages (internal process). **These are the stable grpc-dotnet packages.**
   Repeat all artifact checks in step 4 against the stable version, then push the packages to nuget.org.

10. Create a new release and tag in https://github.com/grpc/grpc-dotnet/releases (by creating the tag from the current release branch).
    This is the stable release. Fill in the release notes and list changes since the previous _stable_ version.

### After the release

On the master branch, increase the version number to indicate the start of a new release cycle (`2.25.0-dev` becomes `2.26.0-dev`). Update all four values from step 3: use the new `-dev` version for `GrpcDotnetVersion` and `CurrentVersion`, and the corresponding numeric version for `GrpcDotnetAssemblyFileVersion` and `CurrentAssemblyFileVersion`.

## Process for patch releases

1. Create patch releases from an existing release branch by bumping the patch component in all four values from step 3. Use the patch's pre-release or stable version for `GrpcDotnetVersion` and `CurrentVersion`, and its numeric version for `GrpcDotnetAssemblyFileVersion` and `CurrentAssemblyFileVersion`. Repeat the artifact checks in step 4.
2. When bug fixes are merged to a release branch, make sure they are synced into the master branch as well; otherwise, the fixes might be missing from the next release cycle.
