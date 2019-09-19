#!/bin/sh
# Copyright 2018 The gRPC Authors
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

# Generates C# API docs using docfx inside docker.
set -ex
cd $(dirname $0)

# cleanup temporary files
rm -rf html obj grpc-gh-pages grpc-main-repo

# clone the grpc/grpc repository to gain access to be able to include Grpc.Core.Api docs
# TODO(jtattermusch): the tag needs to be updated according to the Grpc.Core.Api nuget version used
git clone -b v1.23.0 -o upstream git@github.com:grpc/grpc.git grpc-main-repo

# generate into docfx/html directory by running the "docfx" command using docker
cd ..
docker run --rm -v "$(pwd)":/work -w /work/docfx --user "$(id -u):$(id -g)" -it tsgkadot/docker-docfx:latest docfx --disableGitFeatures
cd docfx

# prepare a clone of "gh-pages" branch where the generated docs are stored
GITHUB_USER="${USER}"
git clone -b gh-pages -o upstream git@github.com:grpc/grpc.git grpc-gh-pages
cd grpc-gh-pages
git remote add origin "git@github.com:${GITHUB_USER}/grpc.git"

# replace old generated docs by the ones we just generated
rm -rf csharp-dotnet
cp -r ../html csharp-dotnet

echo "Done. Go to docfx/grpc-gh-pages git repository and create a pull request to update the generated docs."
