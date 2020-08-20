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

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Dotnet.Cli.Commands;
using Microsoft.Build.Locator;
using NUnit.Framework;

namespace Grpc.Dotnet.Cli.Tests
{
    public class TestBase
    {
        internal static readonly string SourceUrl = "https://contoso.com/greet.proto";

        [OneTimeSetUp]
        public void OneTimeInitialize()
        {
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
        }

        protected HttpClient CreateClient()
        {
            var content = new Dictionary<string, string>()
            {
                {
                    SourceUrl,
@"// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the ""License"");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an ""AS IS"" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

syntax = ""proto3"";

package greet;

// The greeting service definition.
service Greeter {
  // Sends a greeting
  rpc SayHello (HelloRequest) returns (HelloReply) {}
  rpc SayHellos (HelloRequest) returns (stream HelloReply) {}
}

// The request message containing the user's name.
message HelloRequest {
  string name = 1;
}

// The response message containing the greetings
message HelloReply {
  string message = 1;
}"
                },
                // Dummy entry for package version file
                { CommandBase.PackageVersionUrl, "" }
            };

            return CreateClient(content);
        }

        protected HttpClient CreateClient(Dictionary<string, string> content)
        {
            return new HttpClient(new TestMessageHandler(content));
        }

        private class TestMessageHandler : HttpMessageHandler
        {
            private readonly Dictionary<string, string> _contentDictionary;

            public TestMessageHandler(Dictionary<string, string> contentDictionary)
            {
                _contentDictionary = contentDictionary;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var requestUriString = request.RequestUri!.ToString();
                Assert.Contains(requestUriString, _contentDictionary.Keys);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_contentDictionary[requestUriString])
                });
            }
        }
    }
}
