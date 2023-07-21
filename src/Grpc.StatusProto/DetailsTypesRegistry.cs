// Copyright 2023 gRPC authors.
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

using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;

namespace Grpc.StatusProto;

/// <summary>
/// Registry of all the expected types that can be in the "details" of a Status.
/// See https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto
/// for a list of expected messages.
/// </summary>
public static class DetailsTypesRegistry
{
    private static readonly TypeRegistry registry = TypeRegistry.FromMessages(
        new MessageDescriptor[]
        {
            ErrorInfo.Descriptor,
            BadRequest.Descriptor,
            RetryInfo.Descriptor,
            DebugInfo.Descriptor,
            QuotaFailure.Descriptor,
            PreconditionFailure.Descriptor,
            RequestInfo.Descriptor,
            ResourceInfo.Descriptor,
            Help.Descriptor,
            LocalizedMessage.Descriptor
        }
        );

    /// <summary>
    /// Get the registry
    /// </summary>
    /// <returns>the registry</returns>
    public static TypeRegistry GetRegistry() { return registry; }

    /// <summary>
    /// Unpack the "any" message
    /// </summary>
    /// <param name="any">The message to unpack</param>
    /// <returns>
    /// The unpacked message, or null if it was not found in the
    /// registry or could not be unpacked.
    /// </returns>
    public static IMessage? Unpack(Any any)
    {
        return any.Unpack(registry);
    }
}
