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
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Google.Protobuf;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    internal static class HttpResponseMessageExtensions
    {
        public static void AssertIsSuccessfulGrpcRequest(this HttpResponseMessage response)
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues("grpc-encoding").Single());
            Assert.AreEqual("application/grpc", response.Content.Headers.ContentType.MediaType);
        }

        public static async Task<T> GetSuccessfulGrpcMessageAsync<T>(this HttpResponseMessage response) where T : IMessage, new()
        {
            response.AssertIsSuccessfulGrpcRequest();
            return MessageHelpers.AssertReadMessage<T>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
        }
    }
}
