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
    /// Name used in the metadata from the Google.Rpc.Status details
    /// </summary>
    public const string StatusDetailsTrailerName = "grpc-status-details-bin";

    /// <summary>
    /// Get the Google.Rpc.Status from the metadata.
    /// </summary>
    /// <param name="metadata"></param>
    /// <returns>
    /// The found Google.Rpc.Status or null if it was not present or could
    /// the data could not be parsed.
    /// </returns>
    public static Google.Rpc.Status? GetRpcStatus(this Metadata metadata)
    {
        Metadata.Entry? entry = metadata.FirstOrDefault(t => t.Key == StatusDetailsTrailerName);
        if (entry is null)
        {
            return null;
        }
        try
        {
            return Google.Rpc.Status.Parser.ParseFrom(entry.ValueBytes);
        }
        catch
        {
            // If the message is malformed, just report there's no information.
            return null;
        }
    }

    /// <summary>
    /// Add Google.Rpc.Status to the metadata
    /// </summary>
    /// <param name="metadata"></param>
    /// <param name="status">Status to add</param>
    public static void SetRpcStatus(this Metadata metadata, Google.Rpc.Status status)
    {
        metadata.Add(StatusDetailsTrailerName, status.ToByteArray());
    }
}
