#region Copyright notice and license

// Copyright 2026 The gRPC Authors
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

namespace Grpc.Tests.Shared;

internal sealed class TrackingDecompressionStream : MemoryStream
{
    public int BytesRead { get; private set; }
    public bool Disposed { get; private set; }

    public TrackingDecompressionStream(byte[] content) : base(content)
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = base.Read(buffer, offset, count);
        BytesRead += read;
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}
