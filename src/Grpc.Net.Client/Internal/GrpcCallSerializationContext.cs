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

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Compression;
using Grpc.Shared;

namespace Grpc.Net.Client.Internal;

internal sealed class GrpcCallSerializationContext : SerializationContext, IBufferWriter<byte>
{
    private static readonly Status SendingMessageExceedsLimitStatus = new Status(StatusCode.ResourceExhausted, "Sending message exceeds the maximum configured message size.");

    private readonly GrpcCall _call;
    private InternalState _state;
    private int? _payloadLength;
    private ICompressionProvider? _compressionProvider;

    private bool IsDirectSerializationSupported(out int payloadLength)
    {
        // Message can be written directly to the buffer if:
        // - Its length is known.
        // - There is no compression.
        if (_payloadLength != null)
        {
            payloadLength = _payloadLength.Value;
            return _compressionProvider == null;
        }

        payloadLength = 0;
        return false;
    }

    private ArrayBufferWriter<byte>? _bufferWriter;
    private byte[]? _buffer;
    private int _bufferPosition;

    public CallOptions CallOptions { get; set; }

    public GrpcCallSerializationContext(GrpcCall call)
    {
        _call = call;
    }

    private enum InternalState : byte
    {
        Initialized,
        CompleteArray,
        IncompleteBufferWriter,
        CompleteBufferWriter,
    }

    public void Initialize()
    {
        _compressionProvider = ResolveCompressionProvider();

        _payloadLength = null;
        _state = InternalState.Initialized;
        _bufferPosition = 0;
    }

    public void Reset()
    {
        // Release writer and buffer.
        // Stream could be long running and we don't want to hold onto large
        // buffer arrays for a long period of time.
        _bufferWriter = null;

        if (_buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
            _bufferPosition = 0;
        }
    }

    /// <summary>
    /// Obtains the payload from this operation. Error is thrown if complete hasn't been called.
    /// </summary>
    public ReadOnlyMemory<byte> GetWrittenPayload()
    {
        switch (_state)
        {
            case InternalState.CompleteArray:
            case InternalState.CompleteBufferWriter:
                if (_buffer != null)
                {
                    return _buffer.AsMemory(0, _bufferPosition);
                }
                else if (_bufferWriter != null)
                {
                    return _bufferWriter.WrittenMemory;
                }
                break;
        }

        throw new InvalidOperationException("Serialization did not return a payload.");
    }

    private ICompressionProvider? ResolveCompressionProvider()
    {
        CompatibilityHelpers.Assert(
            _call.RequestGrpcEncoding != null,
            "Response encoding should have been calculated at this point.");

        var canCompress =
           GrpcProtocolHelpers.CanWriteCompressed(CallOptions.WriteOptions) &&
           !string.Equals(_call.RequestGrpcEncoding, GrpcProtocolConstants.IdentityGrpcEncoding, StringComparison.Ordinal);

        if (canCompress)
        {
            if (_call.Channel.CompressionProviders.TryGetValue(_call.RequestGrpcEncoding, out var compressionProvider))
            {
                return compressionProvider;
            }

            throw new InvalidOperationException($"Could not find compression provider for '{_call.RequestGrpcEncoding}'.");
        }

        return null;
    }

    public override void SetPayloadLength(int payloadLength)
    {
        switch (_state)
        {
            case InternalState.Initialized:
                _payloadLength = payloadLength;
                break;
            default:
                ThrowInvalidState(_state);
                break;
        }
    }

    public override void Complete(byte[] payload)
    {
        switch (_state)
        {
            case InternalState.Initialized:
                _state = InternalState.CompleteArray;

                GrpcCallLog.SerializedMessage(_call.Logger, _call.RequestType, payload.Length);
                WriteMessage(payload);
                break;
            default:
                ThrowInvalidState(_state);
                break;
        }
    }

    private static void WriteHeader(Span<byte> headerData, int length, bool compress)
    {
        // Compression flag
        headerData[0] = compress ? (byte)1 : (byte)0;

        // Message length
        BinaryPrimitives.WriteUInt32BigEndian(headerData.Slice(1), (uint)length);
    }

    public override IBufferWriter<byte> GetBufferWriter()
    {
        switch (_state)
        {
            case InternalState.Initialized:
                var bufferWriter = ResolveBufferWriter();

                // When writing directly to the buffer the header with message size needs to be written first
                if (IsDirectSerializationSupported(out var payloadLength))
                {
                    EnsureMessageSizeAllowed(payloadLength);

                    WriteHeader(_buffer, payloadLength, compress: false);
                    _bufferPosition += GrpcProtocolConstants.HeaderSize;
                }

                _state = InternalState.IncompleteBufferWriter;
                return bufferWriter;
            case InternalState.IncompleteBufferWriter:
                return ResolveBufferWriter();
            default:
                ThrowInvalidState(_state);
                return default!;
        }
    }

    private IBufferWriter<byte> ResolveBufferWriter()
    {
        if (IsDirectSerializationSupported(out var payloadLength))
        {
            if (_buffer == null)
            {
                _buffer = ArrayPool<byte>.Shared.Rent(GrpcProtocolConstants.HeaderSize + payloadLength);
            }

            return this;
        }
        else if (_bufferWriter == null)
        {
            // Initialize buffer writer with exact length if available.
            // ArrayBufferWriter doesn't allow zero initial length.
            _bufferWriter = payloadLength > 0
                ? new ArrayBufferWriter<byte>(payloadLength)
                : new ArrayBufferWriter<byte>();
        }

        return _bufferWriter;
    }

    private void EnsureMessageSizeAllowed(int payloadLength)
    {
        if (payloadLength > _call.Channel.SendMaxMessageSize)
        {
            throw _call.CreateRpcException(SendingMessageExceedsLimitStatus);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidState(InternalState state)
        => throw new InvalidOperationException("Not valid in the current state: " + state.ToString());

    public override void Complete()
    {
        switch (_state)
        {
            case InternalState.IncompleteBufferWriter:
                _state = InternalState.CompleteBufferWriter;

                if (IsDirectSerializationSupported(out var payloadLength))
                {
                    GrpcCallLog.SerializedMessage(_call.Logger, _call.RequestType, payloadLength);
                }
                else
                {
                    CompatibilityHelpers.Assert(_bufferWriter != null, "Buffer writer has been set to get to this state.");

                    var data = _bufferWriter.WrittenSpan;

                    GrpcCallLog.SerializedMessage(_call.Logger, _call.RequestType, data.Length);
                    WriteMessage(data);
                }
                break;
            default:
                ThrowInvalidState(_state);
                break;
        }
    }

    private void WriteMessage(ReadOnlySpan<byte> data)
    {
        EnsureMessageSizeAllowed(data.Length);

        if (_compressionProvider != null)
        {
            data = CompressMessage(data);
        }

        _buffer = ArrayPool<byte>.Shared.Rent(GrpcProtocolConstants.HeaderSize + data.Length);

        WriteHeader(_buffer, data.Length, compress: _compressionProvider != null);
        _bufferPosition += GrpcProtocolConstants.HeaderSize;

        data.CopyTo(_buffer.AsSpan(GrpcProtocolConstants.HeaderSize));
        _bufferPosition += data.Length;
    }

    private ReadOnlySpan<byte> CompressMessage(ReadOnlySpan<byte> messageData)
    {
        CompatibilityHelpers.Assert(_compressionProvider != null, "Compression provider is not null to get here.");

        GrpcCallLog.CompressingMessage(_call.Logger, _compressionProvider.EncodingName);

        var output = new NonDisposableMemoryStream();

        // Compression stream must be disposed before its content is read.
        // GZipStream writes final Adler32 at the end of the stream on dispose.
        using (var compressionStream = _compressionProvider.CreateCompressionStream(output, CompressionLevel.Fastest))
        {
#if !NETSTANDARD2_0 && !NET462
            compressionStream.Write(messageData);
#else
            var array = messageData.ToArray();
            compressionStream.Write(array, 0, array.Length);
#endif
        }

        return output.GetBuffer().AsSpan(0, (int)output.Length);
    }

    public void Advance(int count)
    {
        if (_buffer != null)
        {
            _bufferPosition += count;
        }
        else
        {
            _bufferWriter!.Advance(count);
        }
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        return _buffer != null ? _buffer.AsMemory(_bufferPosition) : _bufferWriter!.GetMemory(sizeHint);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        return _buffer != null ? _buffer.AsSpan(_bufferPosition) : _bufferWriter!.GetSpan(sizeHint);
    }
}
