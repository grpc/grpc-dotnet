#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Common;
using Grpc.Net.Client;
using Grpc.Reflection.V1Alpha;
using ServerReflectionClient = Grpc.Reflection.V1Alpha.ServerReflection.ServerReflectionClient;

namespace Sample.Clients
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var httpClient = ClientResources.CreateHttpClient("localhost:50051");
            var client = GrpcClient.Create<ServerReflectionClient>(httpClient);

            var response = await SingleRequestAsync(client, new ServerReflectionRequest
            {
                ListServices = "" // Get all services
            });

            Console.WriteLine("Services:");
            foreach (var item in response.ListServicesResponse.Service)
            {
                Console.WriteLine("- " + item.Name);
            }

            Console.WriteLine("Shutting down");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task<ServerReflectionResponse> SingleRequestAsync(ServerReflectionClient client, ServerReflectionRequest request)
        {
            var call = client.ServerReflectionInfo();
            await call.RequestStream.WriteAsync(request);
            Debug.Assert(await call.ResponseStream.MoveNext());

            var response = call.ResponseStream.Current;
            await call.RequestStream.CompleteAsync();
            return response;
        }
    }
}
