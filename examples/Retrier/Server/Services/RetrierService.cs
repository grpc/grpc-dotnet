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
using System.Threading.Tasks;
using Grpc.Core;
using Retry;

namespace Server
{
    public class RetrierService : Retrier.RetrierBase
    {
        private readonly Random _random = new Random();

        public override Task<Response> DeliverPackage(Package request, ServerCallContext context)
        {
            const double deliveryChance = 0.5;
            if (_random.NextDouble() > deliveryChance)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, $"- {request.Name}"));
            }

            return Task.FromResult(new Response
            {
                Message = $"+ {request.Name}"
            });
        }
    }
}
