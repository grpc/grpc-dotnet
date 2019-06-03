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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    internal static class HttpResponseMessageExtensions
    {
        public static void AssertIsSuccessfulGrpcRequest(this HttpResponseMessage response)
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/grpc", response.Content.Headers.ContentType.MediaType);
        }

        public static async Task<T> GetSuccessfulGrpcMessageAsync<T>(this HttpResponseMessage response) where T : IMessage, new()
        {
            response.AssertIsSuccessfulGrpcRequest();
            var data = await response.Content.ReadAsByteArrayAsync().DefaultTimeout();
            response.AssertTrailerStatus();

            return MessageHelpers.AssertReadMessage<T>(data);
        }

        public static void AssertTrailerStatus(this HttpResponseMessage response) => response.AssertTrailerStatus(StatusCode.OK, string.Empty);

        public static void AssertTrailerStatus(this HttpResponseMessage response, StatusCode statusCode, string details)
        {
            HttpResponseHeaders statusHeadersCollection;
            var statusString = GetStatusValue(response.TrailingHeaders, GrpcProtocolConstants.StatusTrailer);
            if (statusString != null)
            {
                statusHeadersCollection = response.TrailingHeaders;
            }
            else
            {
                statusString = GetStatusValue(response.Headers, GrpcProtocolConstants.StatusTrailer);
                statusHeadersCollection = response.Headers;
                if (statusString == null)
                {
                    Assert.Fail($"Count not get {GrpcProtocolConstants.StatusTrailer} from response.");
                }
            }

            Assert.AreEqual(statusCode.ToTrailerString(), statusString, $"Expected grpc-status {statusCode} but got {(StatusCode)Convert.ToInt32(statusString)}");

            // Get message from the same collection as the status
            var messageString = GetStatusValue(statusHeadersCollection, GrpcProtocolConstants.MessageTrailer);
            if (messageString != null)
            {
                Assert.AreEqual(PercentEncodingHelpers.PercentEncode(details), messageString);
            }
            else
            {
                Assert.IsTrue(string.IsNullOrEmpty(details));
            }
        }

        private static string? GetStatusValue(HttpResponseHeaders headers, string name)
        {
            if (headers.TryGetValues(name, out var values))
            {
                return values.Single();
            }

            return null;
        }
    }
}
