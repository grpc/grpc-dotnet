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
using System.IO.Pipelines;
using System.Text;
using Grpc.AspNetCore.Web.Internal;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Web
{
    [TestFixture]
    public class Base64PipeWriterTests
    {
        [Test]
        public void Advance_SmallData_SuccessWithRemainder()
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes("Hello world");

            var testPipe = new Pipe();
            var w = new Base64PipeWriter(testPipe.Writer);
            var innerBuffer = testPipe.Writer.GetMemory();

            // Act
            var buffer = w.GetMemory(initialData.Length);
            initialData.CopyTo(buffer);
            w.Advance(initialData.Length);

            // Assert
            Assert.AreEqual((byte)'l', w._remainderByte0); // remaining bytes, end of "world"
            Assert.AreEqual((byte)'d', w._remainderByte1); // remaining bytes, end of "world"

            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData)).AsSpan(0, 12).ToArray();
            CollectionAssert.AreEqual(innerBuffer.Slice(0, 12).ToArray(), base64Data);
        }

        [Test]
        public async Task Advance_VeryLargeData_SuccessWithRemainder()
        {
            // Arrange
            var s = string.Create<object>(16384, null!, (s, o) =>
            {
                for (var i = 0; i < s.Length; i++)
                {
                    s[i] = Convert.ToChar(i % 10);
                }
            });
            var initialData = Encoding.UTF8.GetBytes(s);

            var testPipe = new Pipe();
            var w = new Base64PipeWriter(testPipe.Writer);
            var innerBuffer = testPipe.Writer.GetMemory();

            // Act
            var buffer = w.GetMemory(initialData.Length);
            initialData.CopyTo(buffer);
            w.Advance(initialData.Length);
            await w.CompleteAsync().AsTask().DefaultTimeout();

            // Assert
            var result = await testPipe.Reader.ReadAsync().AsTask().DefaultTimeout();
            var resultData = result.Buffer.ToArray();
            Assert.AreEqual(21848, result.Buffer.Length);

            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData)).ToArray();
            CollectionAssert.AreEqual(resultData, base64Data);
        }

        [Test]
        public async Task Complete_HasRemainder_WriteRemainder()
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes("Hello world");

            var testPipe = new Pipe();
            var w = new Base64PipeWriter(testPipe.Writer);

            // Act
            var buffer = w.GetMemory(initialData.Length);
            initialData.CopyTo(buffer);
            w.Advance(initialData.Length);

            w.Complete();

            // Assert
            var result = await testPipe.Reader.ReadAsync().AsTask().DefaultTimeout();
            Assert.AreEqual(16, result.Buffer.Length);

            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));
            CollectionAssert.AreEqual(base64Data, result.Buffer.ToArray());
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task Advance_SmallDataMultipleWrites_Success(int size)
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes("Hello world");

            var testPipe = new Pipe();
            var w = new Base64PipeWriter(testPipe.Writer);

            // Act
            foreach (var b in Split(initialData, size))
            {
                var buffer = w.GetMemory(b.Length);
                for (var i = 0; i < b.Length; i++)
                {
                    buffer.Span[i] = b[i];
                }
                w.Advance(b.Length);
            }

            w.Complete();

            // Assert
            var result = await testPipe.Reader.ReadAsync().AsTask().DefaultTimeout();
            Assert.Greater(result.Buffer.Length, 0);

            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));
            var resultData = result.Buffer.ToArray();
            CollectionAssert.AreEqual(base64Data, resultData);
        }

        private static IEnumerable<T[]> Split<T>(T[] array, int size)
        {
            for (var i = 0; i < (float)array.Length / size; i++)
            {
                yield return array.Skip(i * size).Take(size).ToArray();
            }
        }

        [Test]
        public async Task Advance_SmallDataIncompleteWrites_Success()
        {
            // Arrange
            var initialData1 = Encoding.UTF8.GetBytes("Hello");
            var initialData2 = Encoding.UTF8.GetBytes("world");

            var testPipe = new Pipe();
            var w = new Base64PipeWriter(testPipe.Writer);

            // Act
            var buffer = w.GetMemory(initialData1.Length);
            initialData1.CopyTo(buffer);
            w.Advance(initialData1.Length);

            buffer = w.GetMemory(initialData2.Length);
            initialData2.CopyTo(buffer);
            w.Advance(initialData2.Length);

            w.Complete();

            // Assert
            var result = await testPipe.Reader.ReadAsync().AsTask().DefaultTimeout();
            Assert.Greater(result.Buffer.Length, 0);

            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData1.Concat(initialData2).ToArray()));
            CollectionAssert.AreEqual(base64Data, result.Buffer.ToArray());
        }
    }
}
