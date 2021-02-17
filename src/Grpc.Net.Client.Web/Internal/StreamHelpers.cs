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
using System.Diagnostics;
using System.IO;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.Net.Client.Web.Internal
{
    internal static class StreamHelpers
    {
        /// <summary>
        /// WriteAsync uses the best overload for the platform.
        /// </summary>
#if NETSTANDARD2_0
        public static Task WriteAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return stream.WriteAsync(buffer, offset, count, cancellationToken);
        }
#else
        public static ValueTask WriteAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return stream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        }
#endif

        /// <summary>
        /// ReadAsync uses the best overload for the platform. The data must be backed by an array.
        /// </summary>
#if NETSTANDARD2_0
        public static Task<int> ReadAsync(Stream stream, Memory<byte> data, CancellationToken cancellationToken = default)
        {
            var success = MemoryMarshal.TryGetArray<byte>(data, out var segment);
            Debug.Assert(success);
            return stream.ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
        }
#else
        public static ValueTask<int> ReadAsync(Stream stream, Memory<byte> data, CancellationToken cancellationToken = default)
        {
            return stream.ReadAsync(data, cancellationToken);
        }
#endif
    }
}