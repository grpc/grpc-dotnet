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
using System.Net.Http;
using System.Net.Http.Headers;

namespace Grpc.Shared
{
    internal static class TrailingHeadersHelpers
    {
        public static HttpHeaders TrailingHeaders(this HttpResponseMessage responseMessage)
        {
#if !NETSTANDARD2_0
            return responseMessage.TrailingHeaders;
#else
            if (!responseMessage.RequestMessage.Properties.TryGetValue(ResponseTrailersKey, out var headers))
            {
                throw new InvalidOperationException("Couldn't find trailing headers on the response.");
            }
            return (HttpHeaders)headers;
#endif
        }

#if NETSTANDARD2_0
        public static void EnsureTrailingHeaders(this HttpResponseMessage responseMessage)
        {
            if (!responseMessage.RequestMessage.Properties.ContainsKey(ResponseTrailersKey))
            {
                responseMessage.RequestMessage.Properties[ResponseTrailersKey] = new ResponseTrailers();
            }
        }

        public static readonly string ResponseTrailersKey = "__ResponseTrailers";

        private class ResponseTrailers : HttpHeaders
        {
        }
#endif
    }
}
