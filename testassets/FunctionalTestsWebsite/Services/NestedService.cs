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

using System.Threading.Tasks;
using Grpc.Core;
using Nested;

namespace FunctionalTestsWebsite.Services
{
    public class NestedService : Nested.NestedService.NestedServiceBase
    {
        private readonly Greet.Greeter.GreeterClient _greeterClient;

        public NestedService(Greet.Greeter.GreeterClient greeterClient)
        {
            _greeterClient = greeterClient;
        }

        public override async Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            var reply = await _greeterClient.SayHelloAsync(new Greet.HelloRequest { Name = "Nested: " + request.Name });
            return new HelloReply { Message = reply.Message };
        }
    }
}
