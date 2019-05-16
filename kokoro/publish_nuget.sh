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

kokoro/build_nuget.sh

source ./activate.sh

# publish the nugets to nuget dev feed
cd artifacts
for nugetfile in *.nupkg
do
  echo "Going to push $nugetfile"
  set +x  # IMPORTANT: avoid revealing the nuget api key by the command echo
  dotnet nuget push $nugetfile -k $(cat ${KOKORO_GFILE_DIR}/artifactory_grpc_nuget_dev_api_key) --source https://grpc.jfrog.io/grpc/api/nuget/v3/grpc-nuget-dev
  set -ex
done

