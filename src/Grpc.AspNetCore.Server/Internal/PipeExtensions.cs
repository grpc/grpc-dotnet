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
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class PipeExtensions
    {
        private const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
        private const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

        private static readonly Status MessageCancelledStatus = new Status(StatusCode.Internal, "Incoming message cancelled.");
        private static readonly Status AdditionalDataStatus = new Status(StatusCode.Internal, "Additional data after the message received.");
        private static readonly Status IncompleteMessageStatus = new Status(StatusCode.Internal, "Incomplete message.");
        private static readonly Status SendingMessageExceedsLimitStatus = new Status(StatusCode.ResourceExhausted, "Sending message exceeds the maximum configured message size.");
        private static readonly Status ReceivedMessageExceedsLimitStatus = new Status(StatusCode.ResourceExhausted, "Received message exceeds the maximum configured message size.");
        private static readonly Status NoMessageEncodingMessageStatus = new Status(StatusCode.Internal, "Request did not include grpc-encoding value with compressed message.");
        private static readonly Status IdentityMessageEncodingMessageStatus = new Status(StatusCode.Internal, "Request sent 'identity' grpc-encoding value with compressed message.");
        private static Status CreateUnknownMessageEncodingMessageStatus(string unsupportedEncoding, IEnumerable<string> supportedEncodings)
        {
            return new Status(StatusCode.Unimplemented, $"Unsupported grpc-encoding value '{unsupportedEncoding}'. Supported encodings: {string.Join(", ", supportedEncodings)}");
        }

        public static Task WriteMessageAsync<TResponse>(this PipeWriter pipeWriter, TResponse response, HttpContextServerCallContext serverCallContext, Func<TResponse, byte[]> serializer)
        {
            var responsePayload = serializer(response);

            // Flush messages unless WriteOptions.Flags has BufferHint set
            var flush = ((serverCallContext.WriteOptions?.Flags ?? default) & WriteFlags.BufferHint) != WriteFlags.BufferHint;

            return pipeWriter.WriteMessageAsync(responsePayload, serverCallContext, flush);
        }

        public static Task WriteMessageAsync(this PipeWriter pipeWriter, byte[] messageData, HttpContextServerCallContext serverCallContext, bool flush = false)
        {
            if (messageData.Length > serverCallContext.ServiceOptions.SendMaxMessageSize)
            {
                return Task.FromException(new RpcException(SendingMessageExceedsLimitStatus));
            }

            // Must call StartAsync before the first pipeWriter.GetSpan() in WriteHeader
            var response = serverCallContext.HttpContext.Response;
            if (!response.HasStarted)
            {
                var startAsyncTask = response.StartAsync();
                if (!startAsyncTask.IsCompletedSuccessfully)
                {
                    return pipeWriter.WriteMessageCoreAsyncAwaited(messageData, serverCallContext, flush, startAsyncTask);
                }
            }

            return pipeWriter.WriteMessageCoreAsync(messageData, serverCallContext, flush);
        }

        private static async Task WriteMessageCoreAsyncAwaited(this PipeWriter pipeWriter, byte[] messageData, HttpContextServerCallContext serverCallContext, bool flush, Task startAsyncTask)
        {
            await startAsyncTask;
            await pipeWriter.WriteMessageCoreAsync(messageData, serverCallContext, flush);
        }

        private static Task WriteMessageCoreAsync(this PipeWriter pipeWriter, byte[] messageData, HttpContextServerCallContext serverCallContext, bool flush)
        {
            Debug.Assert(serverCallContext.ResponseGrpcEncoding != null);

            var isCompressed =
                serverCallContext.CanWriteCompressed() &&
                !string.Equals(serverCallContext.ResponseGrpcEncoding, GrpcProtocolConstants.IdentityGrpcEncoding, StringComparison.Ordinal);

            if (isCompressed)
            {
                messageData = GrpcProtocolHelpers.CompressMessage(
                    serverCallContext.ResponseGrpcEncoding,
                    serverCallContext.ServiceOptions.ResponseCompressionLevel,
                    serverCallContext.ServiceOptions.CompressionProviders,
                    messageData);
            }

            WriteHeader(pipeWriter, messageData.Length, isCompressed);
            pipeWriter.Write(messageData);

            if (flush)
            {
                serverCallContext.HasBufferedMessage = false;
                return pipeWriter.FlushAsync().GetAsTask();
            }
            else
            {
                // Set flag so buffered message will be written at the end
                serverCallContext.HasBufferedMessage = true;
            }

            return Task.CompletedTask;
        }

        private static void WriteHeader(PipeWriter pipeWriter, int length, bool compress)
        {
            var headerData = pipeWriter.GetSpan(HeaderSize);

            // Compression flag
            headerData[0] = compress ? (byte)1 : (byte)0;

            // Message length
            EncodeMessageLength(length, headerData.Slice(1));

            pipeWriter.Advance(HeaderSize);
        }

        private static void EncodeMessageLength(int messageLength, Span<byte> destination)
        {
            Debug.Assert(destination.Length >= MessageDelimiterSize, "Buffer too small to encode message length.");

            BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)messageLength);
        }

        private static int DecodeMessageLength(ReadOnlySpan<byte> buffer)
        {
            Debug.Assert(buffer.Length >= MessageDelimiterSize, "Buffer too small to decode message length.");

            var result = BinaryPrimitives.ReadUInt32BigEndian(buffer);

            if (result > int.MaxValue)
            {
                throw new IOException("Message too large: " + result);
            }

            return (int)result;
        }

        private static bool TryReadHeader(in ReadOnlySequence<byte> buffer, out bool compressed, out int messageLength)
        {
            if (buffer.Length < HeaderSize)
            {
                compressed = false;
                messageLength = 0;
                return false;
            }

            if (buffer.First.Length >= HeaderSize)
            {
                var headerData = buffer.First.Span.Slice(0, HeaderSize);

                compressed = ReadCompressedFlag(headerData[0]);
                messageLength = DecodeMessageLength(headerData.Slice(1));
            }
            else
            {
                Span<byte> headerData = stackalloc byte[HeaderSize];
                buffer.Slice(0, HeaderSize).CopyTo(headerData);

                compressed = ReadCompressedFlag(headerData[0]);
                messageLength = DecodeMessageLength(headerData.Slice(1));
            }

            return true;
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

        /// <summary>
        /// Read a single message from the pipe reader. Ensure the reader completes without additional data.
        /// </summary>
        /// <param name="input">The request pipe reader.</param>
        /// <param name="context">The request context.</param>
        /// <returns>Complete message data.</returns>
        public static async ValueTask<byte[]> ReadSingleMessageAsync(this PipeReader input, HttpContextServerCallContext context)
        {
            byte[]? completeMessageData = null;

            while (true)
            {
                var result = await input.ReadAsync();
                var buffer = result.Buffer;

                try
                {
                    if (result.IsCanceled)
                    {
                        throw new RpcException(MessageCancelledStatus);
                    }

                    if (!buffer.IsEmpty)
                    {
                        if (completeMessageData != null)
                        {
                            throw new RpcException(AdditionalDataStatus);
                        }

                        if (TryReadMessage(ref buffer, context, out var data))
                        {
                            // Store the message data
                            // Need to verify the request completes with no additional data
                            completeMessageData = data;
                        }
                    }

                    if (result.IsCompleted)
                    {
                        if (completeMessageData != null)
                        {
                            // Finished and the complete message has arrived
                            return completeMessageData;
                        }

                        throw new RpcException(IncompleteMessageStatus);
                    }
                }
                finally
                {
                    // The buffer was sliced up to where it was consumed, so we can just advance to the start.
                    // We mark examined as buffer.End so that if we didn't receive a full frame, we'll wait for more data
                    // before yielding the read again.
                    input.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }

        /// <summary>
        /// Read a message in a stream from the pipe reader. Additional message data is left in the reader.
        /// </summary>
        /// <param name="input">The request pipe reader.</param>
        /// <param name="context">The request content.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Complete message data or null if the stream is complete.</returns>
        public static async ValueTask<byte[]?> ReadStreamMessageAsync(this PipeReader input, HttpContextServerCallContext context, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                var result = await input.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                try
                {
                    if (result.IsCanceled)
                    {
                        throw new RpcException(MessageCancelledStatus);
                    }

                    if (!buffer.IsEmpty)
                    {
                        if (TryReadMessage(ref buffer, context, out var data))
                        {
                            return data;
                        }
                    }

                    if (result.IsCompleted)
                    {
                        if (buffer.Length == 0)
                        {
                            // Finished and there is no more data
                            return null;
                        }

                        throw new RpcException(IncompleteMessageStatus);
                    }
                }
                finally
                {
                    // The buffer was sliced up to where it was consumed, so we can just advance to the start.
                    // We mark examined as buffer.End so that if we didn't receive a full frame, we'll wait for more data
                    // before yielding the read again.
                    input.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }

        private enum ReadMessageResult
        {
            Read,
            Incomplete,
            Stop
        }

        private static bool TryReadMessage(ref ReadOnlySequence<byte> buffer, HttpContextServerCallContext context, [NotNullWhenTrue]out byte[]? message)
        {
            if (!TryReadHeader(buffer, out var compressed, out var messageLength))
            {
                message = null;
                return false;
            }

            if (messageLength > context.ServiceOptions.ReceiveMaxMessageSize)
            {
                throw new RpcException(ReceivedMessageExceedsLimitStatus);
            }

            if (buffer.Length < HeaderSize + messageLength)
            {
                message = null;
                return false;
            }

            // Convert message to byte array
            var messageBuffer = buffer.Slice(HeaderSize, messageLength);
            message = messageBuffer.ToArray();

            if (compressed)
            {
                var encoding = context.GetRequestGrpcEncoding();
                if (encoding == null)
                {
                    throw new RpcException(NoMessageEncodingMessageStatus);
                }
                if (string.Equals(encoding, GrpcProtocolConstants.IdentityGrpcEncoding, StringComparison.Ordinal))
                {
                    throw new RpcException(IdentityMessageEncodingMessageStatus);
                }

                // Performance improvement would be to decompress without converting to an intermediary byte array
                if (!GrpcProtocolHelpers.TryDecompressMessage(encoding, context.ServiceOptions.CompressionProviders, message, out var decompressedMessage))
                {
                    // https://github.com/grpc/grpc/blob/master/doc/compression.md#test-cases
                    // A message compressed by a client in a way not supported by its server MUST fail with status UNIMPLEMENTED,
                    // its associated description indicating the unsupported condition as well as the supported ones. The returned
                    // grpc-accept-encoding header MUST NOT contain the compression method (encoding) used.
                    var supportedEncodings = context.ServiceOptions.CompressionProviders.Select(p => p.EncodingName).ToList();

                    if (!context.HttpContext.Response.HasStarted)
                    {
                        context.HttpContext.Response.Headers[GrpcProtocolConstants.MessageAcceptEncodingHeader] = string.Join(",", supportedEncodings);
                    }

                    throw new RpcException(CreateUnknownMessageEncodingMessageStatus(encoding, supportedEncodings));
                }

                context.ValidateAcceptEncodingContainsResponseEncoding();

                message = decompressedMessage;
            }

            // Update buffer to remove message
            buffer = buffer.Slice(HeaderSize + messageLength);

            return true;
        }
    }
}
