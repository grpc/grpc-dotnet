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
has_failures=false
test_projects=( $( ls test/**/*Tests.csproj ) )
for test_project in "${test_projects[@]}"
do
    base_name=$(basename ${test_project%.*})
    dotnet test $test_project -c Release --no-build --logger "trx;LogFilePrefix=$base_name" --results-directory artifacts/test-results -- NUnit.ConsoleOut=0 || has_failures=true
done

if [ "$has_failures" = true ]; then
    exit 1
fi