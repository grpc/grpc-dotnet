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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Grpc.Core;
using Grpc.Net.Compression;
using Grpc.Shared;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Internal;

internal static partial class StreamExtensions
{
    private static readonly Status ReceivedMessageExceedsLimitStatus = new Status(StatusCode.ResourceExhausted, "Received message exceeds the maximum configured message size.");
    private static readonly Status NoMessageEncodingMessageStatus = new Status(StatusCode.Internal, "Request did not include grpc-encoding value with compressed message.");
    private static readonly Status IdentityMessageEncodingMessageStatus = new Status(StatusCode.Internal, "Request sent 'identity' grpc-encoding value with compressed message.");
    private static Status CreateUnknownMessageEncodingMessageStatus(string unsupportedEncoding, IEnumerable<string> supportedEncodings)
    {
        return new Status(StatusCode.Unimplemented, $"Unsupported grpc-encoding value '{unsupportedEncoding}'. Supported encodings: {string.Join(", ", supportedEncodings)}");
    }

#if !NETSTANDARD2_0 && !NET462
    public static async ValueTask<TResponse?> ReadMessageAsync<TResponse>(
#else
    public static async Task<TResponse?> ReadMessageAsync<TResponse>(
#endif
        this Stream responseStream,
        GrpcCall call,
        Func<DeserializationContext, TResponse> deserializer,
        string grpcEncoding,
        bool singleMessage,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        byte[]? buffer = null;

        try
        {
            GrpcCallLog.ReadingMessage(call.Logger);
            cancellationToken.ThrowIfCancellationRequested();

#if NET6_0_OR_GREATER
            // Start with zero-byte read.
            // A zero-byte read avoids renting buffer until the response is ready. Especially useful for long running streaming calls.
            var readCount = await responseStream.ReadAsync(Memory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            Debug.Assert(readCount == 0);
#endif

            // Buffer is used to read header, then message content.
            // This size was randomly chosen to hopefully be big enough for many small messages.
            // If the message is larger then the array will be replaced when the message size is known.
            buffer = ArrayPool<byte>.Shared.Rent(minimumLength: 4096);

            int read;
            var received = 0;
            while ((read = await responseStream.ReadAsync(buffer.AsMemory(received, GrpcProtocolConstants.HeaderSize - received), cancellationToken).ConfigureAwait(false)) > 0)
            {
                received += read;

                if (received == GrpcProtocolConstants.HeaderSize)
                {
                    break;
                }
            }

            if (received < GrpcProtocolConstants.HeaderSize)
            {
                if (received == 0)
                {
                    GrpcCallLog.NoMessageReturned(call.Logger);
                    return default;
                }

                throw new InvalidDataException("Unexpected end of content while reading the message header.");
            }

            // Read the header first
            // - 1 byte flag for compression
            // - 4 bytes for the content length
            var compressed = ReadCompressedFlag(buffer[0]);
            var length = ReadMessageLength(buffer.AsSpan(1, 4));

            if (length > 0)
            {
                if (length > call.Channel.ReceiveMaxMessageSize)
                {
                    throw call.CreateRpcException(ReceivedMessageExceedsLimitStatus);
                }

                // Replace buffer if the message doesn't fit
                if (buffer.Length < length)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = ArrayPool<byte>.Shared.Rent(length);
                }

                await ReadMessageContentAsync(responseStream, buffer, length, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            ReadOnlySequence<byte> payload;
            if (compressed)
            {
                if (grpcEncoding == null)
                {
                    throw call.CreateRpcException(NoMessageEncodingMessageStatus);
                }
                if (string.Equals(grpcEncoding, GrpcProtocolConstants.IdentityGrpcEncoding, StringComparison.Ordinal))
                {
                    throw call.CreateRpcException(IdentityMessageEncodingMessageStatus);
                }

                // Performance improvement would be to decompress without converting to an intermediary byte array
                if (!TryDecompressMessage(call.Logger, grpcEncoding, call.Channel.CompressionProviders, buffer, length, out var decompressedMessage))
                {
                    var supportedEncodings = new List<string>();
                    supportedEncodings.Add(GrpcProtocolConstants.IdentityGrpcEncoding);
                    supportedEncodings.AddRange(call.Channel.CompressionProviders.Select(c => c.Key));
                    throw call.CreateRpcException(CreateUnknownMessageEncodingMessageStatus(grpcEncoding, supportedEncodings));
                }

                payload = decompressedMessage;
            }
            else
            {
                payload = new ReadOnlySequence<byte>(buffer, 0, length);
            }

            GrpcCallLog.DeserializingMessage(call.Logger, length, typeof(TResponse));

            call.DeserializationContext.SetPayload(payload);
            var message = deserializer(call.DeserializationContext);
            call.DeserializationContext.SetPayload(null);

            if (singleMessage)
            {
                // Check that there is no additional content in the stream for a single message
                // There is no ReadByteAsync on stream. Reuse header array with ReadAsync, we don't need it anymore
                if (await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
                {
                    throw new InvalidDataException("Unexpected data after finished reading message.");
                }
            }

            GrpcCallLog.ReceivedMessage(call.Logger);
            return message;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            // When a deadline expires there can be a race between cancellation and Stream.ReadAsync.
            // If ReadAsync is called after the response is disposed then ReadAsync throws ObjectDisposedException.
            // https://github.com/dotnet/runtime/blob/dfbae37e91c4744822018dde10cbd414c661c0b8/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/Http2Stream.cs#L1479-L1482
            //
            // If ObjectDisposedException is caught and cancellation has happened then rethrow as an OCE.
            // This makes gRPC client correctly report a DeadlineExceeded status.
            throw new OperationCanceledException();
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // Don't write error when user cancels read
            GrpcCallLog.ErrorReadingMessage(call.Logger, ex);
            throw;
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

#if NETSTANDARD2_0 || NET462
    public static Task<int> ReadAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
        {
            return stream.ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
        }
        else
        {
            var array = buffer.ToArray();
            return stream.ReadAsync(array, 0, array.Length, cancellationToken);
        }
    }

    public static Task WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
        {
            return stream.WriteAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
        }
        else
        {
            var array = buffer.ToArray();
            return stream.WriteAsync(array, 0, array.Length, cancellationToken);
        }
    }
#endif

    private static int ReadMessageLength(Span<byte> header)
    {
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);

        if (length > int.MaxValue)
        {
            throw new InvalidDataException("Message too large.");
        }

        return (int)length;
    }

    private static async Task ReadMessageContentAsync(Stream responseStream, Memory<byte> messageData, int length, CancellationToken cancellationToken)
    {
        // Read message content until content length is reached
        var received = 0;
        int read;
        while ((read = await responseStream.ReadAsync(messageData.Slice(received, length - received), cancellationToken).ConfigureAwait(false)) > 0)
        {
            received += read;

            if (received == length)
            {
                break;
            }
        }

        if (received < length)
        {
            throw new InvalidDataException("Unexpected end of content while reading the message content.");
        }
    }

    private static bool TryDecompressMessage(ILogger logger, string compressionEncoding, Dictionary<string, ICompressionProvider> compressionProviders, byte[] messageData, int length, out ReadOnlySequence<byte> result)
    {
        if (compressionProviders.TryGetValue(compressionEncoding, out var compressionProvider))
        {
            GrpcCallLog.DecompressingMessage(logger, compressionProvider.EncodingName);

            var output = new MemoryStream();
            using (var compressionStream = compressionProvider.CreateDecompressionStream(new MemoryStream(messageData, 0, length, writable: true, publiclyVisible: true)))
            {
                compressionStream.CopyTo(output);
            }

            result = new ReadOnlySequence<byte>(output.GetBuffer(), 0, (int)output.Length);
            return true;
        }

        result = default;
        return false;
    }

    private static bool ReadCompressedFlag(byte flag)
    {
        if (flag == 0)
        {
            return false;
        }
        else if (flag == 1)
        {
            return true;
        }
        else
        {
            throw new InvalidDataException("Unexpected compressed flag value in message header.");
        }
    }

    public static async Task WriteMessageAsync<TMessage>(
        this Stream stream,
        GrpcCall call,
        TMessage message,
        Action<TMessage, SerializationContext> serializer,
        CallOptions callOptions)
    {
        // Sync relevant changes here with other WriteMessageAsync
        var serializationContext = call.SerializationContext;
        serializationContext.CallOptions = callOptions;
        serializationContext.Initialize();
        try
        {
            GrpcCallLog.SendingMessage(call.Logger);

            // Serialize message first. Need to know size to prefix the length in the header
            serializer(message, serializationContext);

            // Sending the header+content in a single WriteAsync call has significant performance benefits
            // https://github.com/dotnet/runtime/issues/35184#issuecomment-626304981
            await stream.WriteAsync(serializationContext.GetWrittenPayload(), call.CancellationToken).ConfigureAwait(false);

            GrpcCallLog.MessageSent(call.Logger);
        }
        catch (Exception ex)
        {
            if (!IsCancellationException(ex))
            {
                // Don't write error when user cancels write
                GrpcCallLog.ErrorSendingMessage(call.Logger, ex);
            }

            if (TryCreateCallCompleteException(ex, call, out var statusException))
            {
                throw statusException;
            }

            throw;
        }
        finally
        {
            serializationContext.Reset();
        }
    }

    public static async Task WriteMessageAsync(
        this Stream stream,
        GrpcCall call,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        try
        {
            GrpcCallLog.SendingMessage(call.Logger);

            // Sending the header+content in a single WriteAsync call has significant performance benefits
            // https://github.com/dotnet/runtime/issues/35184#issuecomment-626304981
            await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);

            GrpcCallLog.MessageSent(call.Logger);
        }
        catch (Exception ex)
        {
            if (!IsCancellationException(ex))
            {
                // Don't write error when user cancels write
                GrpcCallLog.ErrorSendingMessage(call.Logger, ex);
            }

            if (TryCreateCallCompleteException(ex, call, out var statusException))
            {
                throw statusException;
            }

            throw;
        }
    }

    private static bool IsCancellationException(Exception ex) => ex is OperationCanceledException or ObjectDisposedException;

    private static bool TryCreateCallCompleteException(Exception originalException, GrpcCall call, [NotNullWhen(true)] out Exception? exception)
    {
        // The call may have been completed while WriteAsync was running and caused WriteAsync to throw.
        // In this situation, report the call's completed status.
        //
        // Replace exception with the status error if:
        // 1. The original exception is one Stream.WriteAsync throws if the call was completed during a write, and
        // 2. The call has already been successfully completed.
        if (IsCancellationException(originalException) &&
            call.CallTask.IsCompletedSuccessfully())
        {
            exception = call.CreateFailureStatusException(call.CallTask.Result);
            return true;
        }

        exception = null;
        return false;
    }
}
