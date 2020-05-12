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
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Compression;
using Grpc.Shared;
using Microsoft.Extensions.Logging;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace Grpc.Net.Client
{
    internal static partial class StreamExtensions
    {
        private static readonly Status SendingMessageExceedsLimitStatus = new Status(StatusCode.ResourceExhausted, "Sending message exceeds the maximum configured message size.");
        private static readonly Status ReceivedMessageExceedsLimitStatus = new Status(StatusCode.ResourceExhausted, "Received message exceeds the maximum configured message size.");
        private static readonly Status NoMessageEncodingMessageStatus = new Status(StatusCode.Internal, "Request did not include grpc-encoding value with compressed message.");
        private static readonly Status IdentityMessageEncodingMessageStatus = new Status(StatusCode.Internal, "Request sent 'identity' grpc-encoding value with compressed message.");
        private static Status CreateUnknownMessageEncodingMessageStatus(string unsupportedEncoding, IEnumerable<string> supportedEncodings)
        {
            return new Status(StatusCode.Unimplemented, $"Unsupported grpc-encoding value '{unsupportedEncoding}'. Supported encodings: {string.Join(", ", supportedEncodings)}");
        }

        public static async ValueTask<TResponse?> ReadMessageAsync<TResponse>(
            this Stream responseStream,
            ILogger logger,
            Func<DeserializationContext, TResponse> deserializer,
            string grpcEncoding,
            int? maximumMessageSize,
            Dictionary<string, ICompressionProvider> compressionProviders,
            bool singleMessage,
            CancellationToken cancellationToken)
            where TResponse : class
        {
            byte[]? buffer = null;

            try
            {
                GrpcCallLog.ReadingMessage(logger);
                cancellationToken.ThrowIfCancellationRequested();

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
                        GrpcCallLog.NoMessageReturned(logger);
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
                    if (length > maximumMessageSize)
                    {
                        throw new RpcException(ReceivedMessageExceedsLimitStatus);
                    }

                    // Replace buffer if the message doesn't fit
                    if (buffer.Length < length)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = ArrayPool<byte>.Shared.Rent(length);
                    }

                    await ReadMessageContent(responseStream, buffer, length, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();

                ReadOnlySequence<byte> payload;
                if (compressed)
                {
                    if (grpcEncoding == null)
                    {
                        throw new RpcException(NoMessageEncodingMessageStatus);
                    }
                    if (string.Equals(grpcEncoding, GrpcProtocolConstants.IdentityGrpcEncoding, StringComparison.Ordinal))
                    {
                        throw new RpcException(IdentityMessageEncodingMessageStatus);
                    }

                    // Performance improvement would be to decompress without converting to an intermediary byte array
                    if (!TryDecompressMessage(logger, grpcEncoding, compressionProviders, buffer, length, out var decompressedMessage))
                    {
                        var supportedEncodings = new List<string>();
                        supportedEncodings.Add(GrpcProtocolConstants.IdentityGrpcEncoding);
                        supportedEncodings.AddRange(compressionProviders.Select(c => c.Key));
                        throw new RpcException(CreateUnknownMessageEncodingMessageStatus(grpcEncoding, supportedEncodings));
                    }

                    payload = decompressedMessage.GetValueOrDefault();
                }
                else
                {
                    payload = new ReadOnlySequence<byte>(buffer, 0, length);
                }

                GrpcCallLog.DeserializingMessage(logger, length, typeof(TResponse));

                var deserializationContext = new DefaultDeserializationContext();
                deserializationContext.SetPayload(payload);
                var message = deserializer(deserializationContext);
                deserializationContext.SetPayload(null);

                if (singleMessage)
                {
                    // Check that there is no additional content in the stream for a single message
                    // There is no ReadByteAsync on stream. Reuse header array with ReadAsync, we don't need it anymore
                    if (await responseStream.ReadAsync(buffer).ConfigureAwait(false) > 0)
                    {
                        throw new InvalidDataException("Unexpected data after finished reading message.");
                    }
                }

                GrpcCallLog.ReceivedMessage(logger);
                return message;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                // Don't write error when user cancels read
                GrpcCallLog.ErrorReadingMessage(logger, ex);
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

        private static int ReadMessageLength(Span<byte> header)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);

            if (length > int.MaxValue)
            {
                throw new InvalidDataException("Message too large.");
            }

            return (int)length;
        }

        private static async Task ReadMessageContent(Stream responseStream, Memory<byte> messageData, int length, CancellationToken cancellationToken)
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

        private static bool TryDecompressMessage(ILogger logger, string compressionEncoding, Dictionary<string, ICompressionProvider> compressionProviders, byte[] messageData, int length, [NotNullWhen(true)]out ReadOnlySequence<byte>? result)
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

            result = null;
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

        // TODO(JamesNK): Reuse serialization context between message writes. Improve client/duplex streaming allocations.
        public static async ValueTask WriteMessageAsync<TMessage>(
            this Stream stream,
            ILogger logger,
            TMessage message,
            Action<TMessage, SerializationContext> serializer,
            string grpcEncoding,
            int? maximumMessageSize,
            Dictionary<string, ICompressionProvider> compressionProviders,
            CallOptions callOptions)
        {
            try
            {
                GrpcCallLog.SendingMessage(logger);

                // Serialize message first. Need to know size to prefix the length in the header
                var serializationContext = new DefaultSerializationContext();
                serializationContext.Reset();
                serializer(message, serializationContext);
                if (!serializationContext.TryGetPayload(out var data))
                {
                    throw new InvalidOperationException("Serialization did not return a payload.");
                }

                GrpcCallLog.SerializedMessage(logger, typeof(TMessage), data.Length);

                if (data.Length > maximumMessageSize)
                {
                    throw new RpcException(SendingMessageExceedsLimitStatus);
                }

                var isCompressed =
                   GrpcProtocolHelpers.CanWriteCompressed(callOptions.WriteOptions) &&
                   !string.Equals(grpcEncoding, GrpcProtocolConstants.IdentityGrpcEncoding, StringComparison.Ordinal);

                if (isCompressed)
                {
                    data = CompressMessage(
                        logger,
                        grpcEncoding,
                        CompressionLevel.Fastest,
                        compressionProviders,
                        data);
                }

                var totalSize = data.Length + GrpcProtocolConstants.HeaderSize;
                var completeData = ArrayPool<byte>.Shared.Rent(totalSize);
                try
                {
                    var buffer = completeData.AsMemory(0, totalSize);

                    WriteHeader(buffer.Span.Slice(0, GrpcProtocolConstants.HeaderSize), isCompressed, data.Length);
                    data.CopyTo(buffer.Slice(GrpcProtocolConstants.HeaderSize));

                    // Sending the header+content in a single WriteAsync call has significant performance benefits
                    // https://github.com/dotnet/runtime/issues/35184#issuecomment-626304981
                    await stream.WriteAsync(buffer, callOptions.CancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(completeData);
                }

                GrpcCallLog.MessageSent(logger);
            }
            catch (Exception ex)
            {
                GrpcCallLog.ErrorSendingMessage(logger, ex);
                throw;
            }
        }

        private static ReadOnlyMemory<byte> CompressMessage(ILogger logger, string compressionEncoding, CompressionLevel? compressionLevel, Dictionary<string, ICompressionProvider> compressionProviders, ReadOnlyMemory<byte> messageData)
        {
            if (compressionProviders.TryGetValue(compressionEncoding, out var compressionProvider))
            {
                GrpcCallLog.CompressingMessage(logger, compressionProvider.EncodingName);

                var output = new MemoryStream();

                // Compression stream must be disposed before its content is read.
                // GZipStream writes final Adler32 at the end of the stream.
                using (var compressionStream = compressionProvider.CreateCompressionStream(output, compressionLevel))
                {
                    compressionStream.Write(messageData.Span);
                }

                return output.GetBuffer().AsMemory(0, (int)output.Length);
            }

            // Should never reach here
            throw new InvalidOperationException($"Could not find compression provider for '{compressionEncoding}'.");
        }

        private static void WriteHeader(Span<byte> headerData, bool isCompressed, int length)
        {
            // Compression flag
            headerData[0] = isCompressed ? (byte)1 : (byte)0;

            // Message length
            EncodeMessageLength(length, headerData.Slice(1, 4));
        }

        private static void EncodeMessageLength(int messageLength, Span<byte> destination)
        {
            Debug.Assert(destination.Length >= GrpcProtocolConstants.MessageDelimiterSize, "Buffer too small to encode message length.");

            BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)messageLength);
        }
    }
}
