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

echo "Starting gRPC-Web interop test containers"

docker-compose -f docker-compose.yml --exit-code-from build grpcweb-server
if [ $? -ne 0 ]
then
  exit $?
fi

docker-compose -f docker-compose.yml --exit-code-from build grpcweb-client
if [ $? -ne 0 ]
then
  exit $?
fi

docker-compose -f docker-compose.yml up -d grpcweb-server
docker-compose -f docker-compose.yml up -d grpcweb-client

sleep 5

echo "Running tests"

cd testassets/InteropTestsGrpcWebWebsite/Tests
npm install && \
    npm test
cd ../../..

echo "Remove all containers"

docker-compose down

echo "gRPC-Web interop tests finished"