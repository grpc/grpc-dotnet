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

using System.Net.Http.Headers;

namespace Grpc.Shared;

internal static class TrailingHeadersHelpers
{
    public static HttpHeaders TrailingHeaders(this HttpResponseMessage responseMessage)
    {
#if !NETSTANDARD2_0 && !NET462
        return responseMessage.TrailingHeaders;
#else
        if (responseMessage.RequestMessage.Properties.TryGetValue(ResponseTrailersKey, out var headers) &&
            headers is HttpHeaders httpHeaders)
        {
            return httpHeaders;
        }

        // App targets .NET Standard 2.0 and the handler hasn't set trailers
        // in RequestMessage.Properties with known key. Return empty collection.
        // Client call will likely fail because it is unable to get a grpc-status.
        return ResponseTrailers.Empty;
#endif
    }

#if NETSTANDARD2_0 || NET462
    public static void EnsureTrailingHeaders(this HttpResponseMessage responseMessage)
    {
        if (!responseMessage.RequestMessage.Properties.ContainsKey(ResponseTrailersKey))
        {
            responseMessage.RequestMessage.Properties[ResponseTrailersKey] = new ResponseTrailers();
        }
    }

    public static readonly string ResponseTrailersKey = "__ResponseTrailers";

    private sealed class ResponseTrailers : HttpHeaders
    {
        public static readonly ResponseTrailers Empty = new ResponseTrailers();
    }
#endif
}
