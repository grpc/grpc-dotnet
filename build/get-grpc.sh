#!/usr/bin/env bash

set -euo pipefail

# variables
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
OBJDIR="$DIR/obj"
version_url="https://raw.githubusercontent.com/grpc/grpc/master/src/csharp/Grpc.Core/Version.csproj.include"
builds_url="https://packages.grpc.io/"
version_path="$OBJDIR/version.xml"
builds_path="$OBJDIR/builds.xml"
grpc_lock_path="$DIR/grpc-lock.txt"
feed_path="$DIR/feed/"
dependencies_path="$DIR/dependencies.props"
packages=("Grpc.Core" "Grpc.Core.Api" "Grpc.Tools")
upgrade=false

# functions
ensure_dir() {
    [ -d $1 ] || mkdir $1
}

# main

while [[ $# -gt 0 ]]; do
    case $1 in
        -u|--[Uu]pgrade)
            upgrade=true
            ;;
        --)
            shift
            break
            ;;
        *)
            break
            ;;
    esac
    shift
done

if [ ! -f "$grpc_lock_path" ] || [ "$upgrade" = true ]; then
    if ! [ -x "$(command -v xmllint)" ]; then
        echo 'Error: xmllint is not installed.' >&2
        exit 1
    fi

    # download manifests
    ensure_dir $OBJDIR

    echo "Downloading manifest: $version_url => $version_path"
    curl -sSL -o $version_path $version_url
    echo "Downloading manifest: $builds_url => $builds_path"
    curl -sSL -o $builds_path $builds_url

    # retrive package version
    package_version=$(xmllint --xpath string\(/Project/PropertyGroup/GrpcCsharpVersion\) $version_path)
    echo "Latest package version: $package_version"

    # retrieve nupkg path
    nupkg_path=$(xmllint --xpath string\(/packages/builds/build[1]/@path\) $builds_path )
    nupkg_path=${nupkg_path%"index.xml"}

    # write to lock file
    printf "$package_version $nupkg_path\n" > $grpc_lock_path

    # update dependencies
    for package in ${packages[@]};
    do
        # update depedencies.props
        sed -i "s/\(<${package//.}PackageVersion>\)[^<>]*\(<\/${package//.}PackageVersion\)/\1$package_version\2/" $dependencies_path
    done
fi

# read from lock file
read package_version nupkg_path < $grpc_lock_path

# update dependencies
ensure_dir $feed_path
for package in ${packages[@]};
do
    # download packages
    package_url="${builds_url}${nupkg_path}csharp/${package}.${package_version}.nupkg"
    echo "Downloading $package_url"
    (cd $feed_path && curl -sSL -O $package_url)
done
