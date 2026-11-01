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

using System.IO.Pipelines;
using Google.Protobuf;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Tests.Shared;

namespace Grpc.AspNetCore.Microbenchmarks.Internal;

internal static class MessageHelpers
{
    // Base64 of random bytes compresses to roughly 75% with gzip, similar to moderately compressible payloads.
    // Repeated text compresses to almost nothing, which would hide the cost of buffering compressed output.
    public static string CreateCompressibleContent(int length)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);

        return Convert.ToBase64String(bytes).Substring(0, length);
    }

    public static void WriteMessage<T>(Stream stream, T message, HttpContextServerCallContext? callContext = null)
        where T : class, IMessage
    {
        var pipeWriter = PipeWriter.Create(stream);

        PipeExtensions.WriteStreamedMessageAsync(pipeWriter, message, callContext ?? HttpContextServerCallContextHelper.CreateServerCallContext(), (r, c) => c.Complete(r.ToByteArray())).GetAwaiter().GetResult();
    }
}
