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
using Grpc.Core;

namespace Grpc.StatusProto;

/// <summary>
/// Extension methods for the Grpc.Core.Metadata
/// </summary>
public static class MetadataExtensions
{
    /// <summary>
    /// Name of key in the metadata for the binary encoding of
    /// <see cref="Google.Rpc.Status"/>
    /// </summary>
    public const string StatusDetailsTrailerName = "grpc-status-details-bin";

    /// <summary>
    /// Get the <see cref="Google.Rpc.Status"/> from the metadata.
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    /// <param name="metadata"></param>
    /// <param name="throwOnParseError">if true then <see cref="Google.Protobuf.InvalidProtocolBufferException"/>
    /// is thrown if the metadata cannot be parsed. Otherwise null is returned on a parsing error.</param>
    /// <returns>
    /// The found <see cref="Google.Rpc.Status"/> or null if it was
    /// not present or could the data could not be parsed.
    /// </returns>
    public static Google.Rpc.Status? GetRpcStatus(this Metadata metadata, bool throwOnParseError = false)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var entry = metadata.Get(StatusDetailsTrailerName);
        if (entry is null)
        {
            return null;
        }
        try
        {
            return Google.Rpc.Status.Parser.ParseFrom(entry.ValueBytes);
        }
        catch when (!throwOnParseError)
        {
            // By default if the message is malformed, just report there's no information.
            return null;
        }
    }

    /// <summary>
    /// Add <see cref="Google.Rpc.Status"/> to the metadata.
    /// Any existing status in the metadata will be overwritten.
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    /// <param name="metadata"></param>
    /// <param name="status">Status to add</param>
    public static void SetRpcStatus(this Metadata metadata, Google.Rpc.Status status)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (status == null)
        {
            throw new ArgumentNullException(nameof(status));
        }

        var entry = metadata.Get(StatusDetailsTrailerName);
        while (entry is not null)
        {
            metadata.Remove(entry);
            entry = metadata.Get(StatusDetailsTrailerName);
        }
        metadata.Add(StatusDetailsTrailerName, status.ToByteArray());
    }
}
