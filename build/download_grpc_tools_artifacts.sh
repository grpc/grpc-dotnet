#!/usr/bin/env bash

set -eux

# variables
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
input_artifacts_path="$DIR/../input_artifacts"

# prepare the input_artifacts directory
mkdir -p ${input_artifacts_path}
rm -rf ${input_artifacts_path}/*

# download the artifacts required for building the Grpc.Tools nuget.
# The download link points to results of the "build_packages" phase
# from the latest grpc/grpc release.
# TODO(jtattermusch): update the link to point to the build results of an actual grpc/grpc release.
curl --fail -sSL -o ${input_artifacts_path}/csharp_grpc_tools_artifacts.zip https://storage.googleapis.com/grpc-testing-kokoro-prod/test_result_public/prod/grpc/core/pull_request/linux/grpc_distribtests_csharp/3001/20220405-060112/github/grpc/artifacts/csharp_grpc_tools_artifacts.zip

pushd ${input_artifacts_path}

# unpack the artifacts
unzip csharp_grpc_tools_artifacts.zip
