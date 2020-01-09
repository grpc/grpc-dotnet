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
        private const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
        private const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

        private static readonly Status SendingMessageExceedsLimitStatus = new Status(StatusCode.ResourceExhausted, "Sending message exceeds the maximum configured message size.");
        private static readonly Status ReceivedMessageExceedsLimitStatus = new Status(StatusCode.ResourceExhausted, "Received message exceeds the maximum configured message size.");
        private static readonly Status NoMessageEncodingMessageStatus = new Status(StatusCode.Internal, "Request did not include grpc-encoding value with compressed message.");
        private static readonly Status IdentityMessageEncodingMessageStatus = new Status(StatusCode.Internal, "Request sent 'identity' grpc-encoding value with compressed message.");
        private static Status CreateUnknownMessageEncodingMessageStatus(string unsupportedEncoding, IEnumerable<string> supportedEncodings)
        {
            return new Status(StatusCode.Unimplemented, $"Unsupported grpc-encoding value '{unsupportedEncoding}'. Supported encodings: {string.Join(", ", supportedEncodings)}");
        }

        private static async Task<(uint length, bool compressed)?> ReadHeaderAsync(Stream responseStream, Memory<byte> header, CancellationToken cancellationToken)
        {
            int read;
            var received = 0;
            while ((read = await responseStream.ReadAsync(header.Slice(received, header.Length - received), cancellationToken).ConfigureAwait(false)) > 0)
            {
                received += read;

                if (received == header.Length)
                {
                    break;
                }
            }

            if (received < header.Length)
            {
                if (received == 0)
                {
                    return null;
                }

                throw new InvalidDataException("Unexpected end of content while reading the message header.");
            }

            var compressed = ReadCompressedFlag(header.Span[0]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header.Span.Slice(1));

            return (length, compressed);
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
            try
            {
                GrpcCallLog.ReadingMessage(logger);
                cancellationToken.ThrowIfCancellationRequested();

                // Read the header first
                // - 1 byte flag for compression
                // - 4 bytes for the content length
                var header = new byte[HeaderSize];

                var headerDetails = await ReadHeaderAsync(responseStream, header, cancellationToken).ConfigureAwait(false);

                if (headerDetails == null)
                {
                    GrpcCallLog.NoMessageReturned(logger);
                    return default;
                }

                var length = headerDetails.Value.length;
                var compressed = headerDetails.Value.compressed;

                if (length > int.MaxValue)
                {
                    throw new InvalidDataException("Message too large.");
                }

                if (length > maximumMessageSize)
                {
                    throw new RpcException(ReceivedMessageExceedsLimitStatus);
                }

                var messageData = await ReadMessageContent(responseStream, length, cancellationToken).ConfigureAwait(false);

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
                    if (!TryDecompressMessage(logger, grpcEncoding, compressionProviders, messageData, out var decompressedMessage))
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
                    payload = new ReadOnlySequence<byte>(messageData);
                }

                GrpcCallLog.DeserializingMessage(logger, messageData.Length, typeof(TResponse));

                var deserializationContext = new DefaultDeserializationContext();
                deserializationContext.SetPayload(payload);
                var message = deserializer(deserializationContext);
                deserializationContext.SetPayload(null);

                if (singleMessage)
                {
                    // Check that there is no additional content in the stream for a single message
                    // There is no ReadByteAsync on stream. Reuse header array with ReadAsync, we don't need it anymore
                    if (await responseStream.ReadAsync(header).ConfigureAwait(false) > 0)
                    {
                        throw new InvalidDataException("Unexpected data after finished reading message.");
                    }
                }

                GrpcCallLog.ReceivedMessage(logger);
                return message;
            }
            catch (Exception ex)
            {
                GrpcCallLog.ErrorReadingMessage(logger, ex);
                throw;
            }
        }

        private static async Task<byte[]> ReadMessageContent(Stream responseStream, uint length, CancellationToken cancellationToken)
        {
            // Read message content until content length is reached
            byte[] messageData;
            if (length > 0)
            {
                var received = 0;
                var read = 0;
                messageData = new byte[length];
                while ((read = await responseStream.ReadAsync(messageData.AsMemory(received, messageData.Length - received), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += read;

                    if (received == messageData.Length)
                    {
                        break;
                    }
                }
            }
            else
            {
                messageData = Array.Empty<byte>();
            }

            return messageData;
        }

        private static bool TryDecompressMessage(ILogger logger, string compressionEncoding, Dictionary<string, ICompressionProvider> compressionProviders, byte[] messageData, [NotNullWhen(true)]out ReadOnlySequence<byte>? result)
        {
            if (compressionProviders.TryGetValue(compressionEncoding, out var compressionProvider))
            {
                GrpcCallLog.DecompressingMessage(logger, compressionProvider.EncodingName);

                var output = new MemoryStream();
                using (var compressionStream = compressionProvider.CreateDecompressionStream(new MemoryStream(messageData)))
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

                await WriteHeaderAsync(stream, data.Length, isCompressed, callOptions.CancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(data, callOptions.CancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(callOptions.CancellationToken).ConfigureAwait(false);

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

        private static ValueTask WriteHeaderAsync(Stream stream, int length, bool compress, CancellationToken cancellationToken)
        {
            var headerData = new byte[HeaderSize];

            // Compression flag
            headerData[0] = compress ? (byte)1 : (byte)0;

            // Message length
            EncodeMessageLength(length, headerData.AsSpan(1));

            return stream.WriteAsync(headerData.AsMemory(0, headerData.Length), cancellationToken);
        }

        private static void EncodeMessageLength(int messageLength, Span<byte> destination)
        {
            Debug.Assert(destination.Length >= MessageDelimiterSize, "Buffer too small to encode message length.");

            BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)messageLength);
        }
    }
}
