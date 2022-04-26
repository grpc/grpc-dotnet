#!/bin/bash
# Copyright 2022 The gRPC Authors
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# Update Grpc.Core.Api's VersionInfo.cs with versions from version.props

set -e

cd "$(dirname "$0")"

# retrieve version strings from version.props
PACKAGE_VERSION=$(grep -o '<GrpcDotnetVersion>.*</GrpcDotnetVersion>' version.props | sed 's/<GrpcDotnetVersion>//' | sed 's/<\/GrpcDotnetVersion>//')
ASSEMBLY_VERSION=$(grep -o '<GrpcDotnetAssemblyVersion>.*</GrpcDotnetAssemblyVersion>' version.props | sed 's/<GrpcDotnetAssemblyVersion>//' | sed 's/<\/GrpcDotnetAssemblyVersion>//')
ASSEMBLY_FILE_VERSION=$(grep -o '<GrpcDotnetAssemblyFileVersion>.*</GrpcDotnetAssemblyFileVersion>' version.props | sed 's/<GrpcDotnetAssemblyFileVersion>//' | sed 's/<\/GrpcDotnetAssemblyFileVersion>//')

# update the contents of src/Grpc.Core.Api/VersionInfo.cs with the version strings
sed -i "s/CurrentVersion = \".*\"/CurrentVersion = \"${PACKAGE_VERSION}\"/g" ../src/Grpc.Core.Api/VersionInfo.cs
sed -i "s/CurrentAssemblyVersion = \".*\"/CurrentAssemblyVersion = \"${ASSEMBLY_VERSION}\"/g" ../src/Grpc.Core.Api/VersionInfo.cs
sed -i "s/CurrentAssemblyFileVersion = \".*\"/CurrentAssemblyFileVersion = \"${ASSEMBLY_FILE_VERSION}\"/g" ../src/Grpc.Core.Api/VersionInfo.cs

echo "Updated version strings in src/Grpc.Core.Api/VersionInfo.cs"