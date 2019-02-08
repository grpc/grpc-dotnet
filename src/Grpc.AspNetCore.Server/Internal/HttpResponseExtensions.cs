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
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class HttpResponseExtensions
    {
        public static void ConsolidateTrailers(this HttpResponse httpResponse, HttpContextServerCallContext context)
        {
            if (context.HasResponseTrailers)
            {
                foreach (var trailer in context.ResponseTrailers)
                {
                    if (trailer.IsBinary)
                    {
                        httpResponse.AppendTrailer(trailer.Key, Convert.ToBase64String(trailer.ValueBytes));
                    }
                    else
                    {
                        httpResponse.AppendTrailer(trailer.Key, trailer.Value);
                    }
                }
            }

            // Append status trailers, these overwrite any existing status trailers set via ServerCallContext.ResponseTrailers
            httpResponse.AppendTrailer(GrpcProtocolConstants.StatusTrailer, context.Status.StatusCode.ToTrailerString());
            httpResponse.AppendTrailer(GrpcProtocolConstants.MessageTrailer, context.Status.Detail);
        }
    }
}
