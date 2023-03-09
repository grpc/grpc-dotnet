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

using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Server.Internal;

internal static class HttpResponseExtensions
{
    public static void ConsolidateTrailers(this HttpResponse httpResponse, HttpContextServerCallContext context)
    {
        var trailersDestination = GrpcProtocolHelpers.GetTrailersDestination(httpResponse);

        if (context.HasResponseTrailers)
        {
            foreach (var trailer in context.ResponseTrailers)
            {
                var value = (trailer.IsBinary) ? Convert.ToBase64String(trailer.ValueBytes) : trailer.Value;
                try
                {
                    trailersDestination.Append(trailer.Key, value);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error adding response trailer '{trailer.Key}'.", ex);
                }
            }
        }

        // Append status trailers, these overwrite any existing status trailers set via ServerCallContext.ResponseTrailers
        GrpcProtocolHelpers.SetStatus(trailersDestination, context.Status);
    }
}
