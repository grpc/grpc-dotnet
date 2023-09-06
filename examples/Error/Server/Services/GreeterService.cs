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

using System.Runtime.CompilerServices;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Greet;
using Grpc.Core;
using Grpc.StatusProto;

namespace Server
{
    public class GreeterService : Greeter.GreeterBase
    {
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            ArgumentNotNullOrEmpty(request.Name);

            return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
        }

        private static void ArgumentNotNullOrEmpty(string value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new Google.Rpc.Status
                {
                    Code = (int)Code.InvalidArgument,
                    Message = "Bad request",
                    Details =
                    {
                        Any.Pack(new BadRequest
                        {
                            FieldViolations =
                            {
                                new BadRequest.Types.FieldViolation
                                {
                                    Field = paramName,
                                    Description = "Value is null or empty"
                                }
                            }
                        })
                    }
                }.ToRpcException();
            }
        }
    }
}
