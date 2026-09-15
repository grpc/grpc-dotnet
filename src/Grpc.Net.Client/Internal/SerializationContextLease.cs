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

namespace Grpc.Net.Client.Internal;

/// <summary>
/// A temporary handle to a <see cref="GrpcCallSerializationContext"/> for a single usage.
///
/// Callers should:<br/>
/// - call <see cref="MarkReusable"/> only after successful operation.
///   Serialization context will be reused by next renter.<br/>
/// - call <see cref="Dispose"/> once completed <see cref="Context"/> usage.
/// </summary>
internal struct SerializationContextLease : IDisposable
{
    private readonly GrpcCall _call;
    private GrpcCallSerializationContext? _context;
    private bool _reusable;

    internal SerializationContextLease(GrpcCall call, GrpcCallSerializationContext context)
    {
        _call = call;
        _context = context;
        _reusable = false;
    }

    public readonly GrpcCallSerializationContext Context => _context!;

    /// <summary>
    /// Marks the context as safe to hand back for reuse once disposed.
    /// </summary>
    public void MarkReusable() => _reusable = true;

    public void Dispose()
    {
        var context = _context;
        if (context == null)
        {
            return;
        }

        _context = null;
        context.Reset(); // Always release the rented payload buffer.

        if (_reusable)
        {
            _call.ReturnSerializationContext(context);
        }
    }
}
