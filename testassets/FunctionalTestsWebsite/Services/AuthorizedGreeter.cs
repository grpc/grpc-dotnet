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

using System.Linq;
using System.Threading.Tasks;
using Authorize;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace FunctionalTestsWebsite.Services
{
    [Authorize(JwtBearerDefaults.AuthenticationScheme)]
    public class AuthorizedGreeter : Authorize.AuthorizedGreeter.AuthorizedGreeterBase
    {
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            var claims = context.GetHttpContext().User.Claims.ToList();

            var reply = new HelloReply();
            reply.Message = "Hello " + request.Name;
            foreach (var claim in claims)
            {
                reply.Claims.Add(claim.Type, claim.Value);
            }

            return Task.FromResult(reply);
        }
    }
}
