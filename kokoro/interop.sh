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

# change to grpc-dotnet repo root
cd $(dirname $0)/..

# The run_interop_tests.py testing scripts lives in the main gRPC repository
# and runs tests for other languages from there.
git clone https://github.com/grpc/grpc ./../grpc

# change to grpc/grpc repo root
cd ../grpc

source tools/internal_ci/helper_scripts/prepare_build_linux_rc

tools/run_tests/run_interop_tests.py -l aspnetcore c++ -s aspnetcore c++ --use_docker -t -j 8
