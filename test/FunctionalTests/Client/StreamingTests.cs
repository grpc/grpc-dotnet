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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using NUnit.Framework;
using Streaming;

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
            var client = GrpcClient.Create<StreamService.StreamServiceClient>(Fixture.Client);

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
    }
}
