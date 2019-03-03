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
using System.Buffers.Binary;
using System.IO;

namespace Grpc.NetCore.HttpClient
{
    internal static class StreamExtensions
    {
        public static TResponse ReadSingleMessage<TResponse>(this Stream responseStream, Func<byte[], TResponse> deserializer)
        {
            if (responseStream.ReadByte() != 0)
            {
                throw new InvalidOperationException("Compressed response not yet supported");
            }

            var lengthBytes = new byte[4];
            responseStream.Read(lengthBytes, 0, 4);
            var length = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
            if (length > int.MaxValue)
            {
                throw new InvalidOperationException("message too large");
            }

            var responseBytes = new byte[length];
            responseStream.Read(responseBytes, 0, (int)length);

            return deserializer(responseBytes);
        }
    }
}
