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

source activate.sh

echo "Building solution"

dotnet build -c Release

echo "Building examples"

example_solutions=( $( ls examples/**/*.sln ) )

for example_solution in "${example_solutions[@]}"
do
    dotnet build $example_solution -c Release
done

echo "Testing solution"

test_projects=( $( ls test/**/*Tests.csproj ) )

for test_project in "${test_projects[@]}"
do
    # https://github.com/microsoft/vstest/issues/2080#issuecomment-539879345
    dotnet test $test_project -c Release -v n --no-build < /dev/null
done

echo "Tests finished"

# Temporarily disable while using nightly .NET SDK build
# source grpcweb_interoptests.sh