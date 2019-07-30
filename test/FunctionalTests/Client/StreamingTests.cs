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
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Streaming;
using Unimplemented;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class StreamingTests : FunctionalTestBase
    {
        [Test]
        public async Task DuplexStream_SendLargeFileBatchedAndRecieveLargeFileBatched_Success()
        {
            // Arrange
            var data = new byte[1024 * 1024 * 1]; // 1 MB
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)i; // Will loop around back to zero
            }
            var client = GrpcClient.Create<StreamService.StreamServiceClient>(Fixture.Client, LoggerFactory);

            // Act
            var call = client.DuplexData();

            var sent = 0;
            while (sent < data.Length)
            {
                const int BatchSize = 1024 * 64; // 64 KB

                var writeCount = Math.Min(data.Length - sent, BatchSize);
                var finalWrite = sent + writeCount == data.Length;
                await call.RequestStream.WriteAsync(new DataMessage
                {
                    Data = ByteString.CopyFrom(data, sent, writeCount),
                    FinalSegment = finalWrite
                }).DefaultTimeout();

                sent += writeCount;
            }

            var ms = new MemoryStream();
            while (await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout())
            {
                ms.Write(call.ResponseStream.Current.Data.Span);
            }

            // Assert
            CollectionAssert.AreEqual(data, ms.ToArray());
        }

        [Test]
        public async Task ClientStream_SendLargeFileBatchedAndRecieveLargeFileBatched_Success()
        {
            // Arrange
            var total = 1024 * 1024 * 64; // 64 MB
            var data = new byte[1024 * 64]; // 64 KB
            var client = GrpcClient.Create<StreamService.StreamServiceClient>(Fixture.Client, LoggerFactory);
            var dataMessage = new DataMessage
            {
                Data = ByteString.CopyFrom(data)
            };

            // Act
            var call = client.ClientStreamedData();

            var sent = 0;
            while (sent < total)
            {
                var writeCount = Math.Min(total - sent, data.Length);
                DataMessage m;
                if (writeCount == data.Length)
                {
                    m = dataMessage;
                }
                else
                {
                    m = new DataMessage
                    {
                        Data = ByteString.CopyFrom(data, 0, writeCount)
                    };
                }

                await call.RequestStream.WriteAsync(m).DefaultTimeout();

                sent += writeCount;
            }

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var response = await call.ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(total, response.Size);
        }

        [Test]
        public async Task DuplexStream_SendToUnimplementedMethod_ThrowError()
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "ErrorSendingMessage" &&
                    writeContext.State.ToString() == "Error sending message.")
                {
                    return true;
                }

                return false;
            });

            // Arrange
            var client = GrpcClient.Create<UnimplementedService.UnimplementedServiceClient>(Fixture.Client, LoggerFactory);

            // Act
            var call = client.DuplexData();

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () =>
            {
                await call.RequestStream.WriteAsync(new UnimplementeDataMessage
                {
                    Data = ByteString.CopyFrom(new byte[1024 * 64])
                }).DefaultTimeout();

                await call.RequestStream.WriteAsync(new UnimplementeDataMessage
                {
                    Data = ByteString.CopyFrom(new byte[1024 * 64])
                }).DefaultTimeout();
            });

            // Assert
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        }
    }
}
