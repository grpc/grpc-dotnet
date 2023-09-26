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

using Google.Protobuf.Reflection;
using Google.Rpc;

namespace Grpc.StatusProto;

/// <summary>
/// Registry of the <see href="https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto">
/// standard set of error types</see> defined in the richer error model developed and used by Google.
/// These can be sepcified in the <see cref="Google.Rpc.Status.Details"/>.
/// Note: experimental API that can change or be removed without any prior notice.
/// </summary>
public static class StandardErrorTypeRegistry
{
    // TODO(tonydnewell) maybe move this class to Google.Api.CommonProtos

    private static readonly TypeRegistry _registry = TypeRegistry.FromMessages(
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
        });

    /// <summary>
    /// Get the registry
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    public static TypeRegistry Registry => _registry;
}
