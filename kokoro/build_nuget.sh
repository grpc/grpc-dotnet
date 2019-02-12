#!/bin/bash
# Copyright 2019 The gRPC Authors
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

set -ex

# change to grpc repo root
cd $(dirname $0)/..

# Install dotnet SDK
# copied from .travis.yml
curl -o dotnet-sdk.tar.gz -sSL https://download.visualstudio.microsoft.com/download/pr/efa6dde9-a5ee-4322-b13c-a2a02d3980f0/dad445eba341c1d806bae5c8afb47015/dotnet-sdk-3.0.100-preview-010184-linux-x64.tar.gz
mkdir -p $PWD/dotnet
tar zxf dotnet-sdk.tar.gz -C $PWD/dotnet
export PATH="$PWD/dotnet:$PATH"

# TODO(jtattermusch): remove this before release, otherwise 
# references will be broken.
./build/get-grpc.sh

mkdir -p artifacts

# TODO(jtattermusch): set the package version in csproj files.
(cd src/Grpc.AspNetCore.Server && dotnet pack -p:PackageVersion=0.0.1-dev --configuration Release --output ../../artifacts)
