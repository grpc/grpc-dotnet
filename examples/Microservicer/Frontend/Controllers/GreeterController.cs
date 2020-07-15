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
using Greet;
using Microsoft.AspNetCore.Mvc;

namespace Frontend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GreeterController : ControllerBase
    {
        private readonly Greeter.GreeterClient _client;

        public GreeterController(Greeter.GreeterClient client)
        {
            _client = client;
        }

        [HttpGet("{name}")]
        public async Task<ActionResult> SayHello(string name)
        {
            var reply = await _client.SayHelloAsync(new HelloRequest { Name = name });

            return Ok(new { message = reply.Message });
        }
    }
}
