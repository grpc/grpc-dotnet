using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal.Web;
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
            Assert.AreEqual((byte)'l', innerBuffer.Span[12]); // remaining bytes, end of "world"
            Assert.AreEqual((byte)'d', innerBuffer.Span[13]); // remaining bytes, end of "world"

            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData)).AsSpan(0, 12).ToArray();
            CollectionAssert.AreEqual(innerBuffer.Slice(0, 12).ToArray(), base64Data);
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

        [Test]
        public async Task Advance_SmallDataMultipleWrites_Success()
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes("Hello world");

            var testPipe = new Pipe();
            var w = new Base64PipeWriter(testPipe.Writer);

            // Act
            foreach (var b in initialData)
            {
                var buffer = w.GetMemory(1);
                buffer.Span[0] = b;
                w.Advance(1);
            }

            w.Complete();

            // Assert
            var result = await testPipe.Reader.ReadAsync().AsTask().DefaultTimeout();
            Assert.Greater(result.Buffer.Length, 0);

            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));
            CollectionAssert.AreEqual(base64Data, result.Buffer.ToArray());
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
