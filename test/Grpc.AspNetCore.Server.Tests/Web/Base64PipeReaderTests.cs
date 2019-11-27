using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal.Web;
using Grpc.Reflection;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Web
{
    [TestFixture]
    public class Base64PipeReaderTests
    {
        [Test]
        public async Task ReadAsync_SmallData_Success()
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes("Hello world");
            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));
            var testPipe = new Pipe();
            await testPipe.Writer.WriteAsync(base64Data);
            var r = new Base64PipeReader(testPipe.Reader);

            // Act
            var result = await r.ReadAsync();

            // Assert
            Assert.Greater(result.Buffer.Length, 0);

            CollectionAssert.AreEqual(initialData, result.Buffer.ToArray());
        }

        [Test]
        public async Task ReadAsync_MultipleWrites_Success()
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes("Hello world");
            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));
            var testPipe = new Pipe();
            await testPipe.Writer.WriteAsync(base64Data.AsMemory(0, 2));
            var r = new Base64PipeReader(testPipe.Reader);

            // Act
            var resultTask = r.ReadAsync();

            Assert.IsFalse(resultTask.IsCompleted);

            await testPipe.Writer.WriteAsync(base64Data.AsMemory(2));

            var result = await resultTask;

            // Assert
            Assert.Greater(result.Buffer.Length, 0);

            CollectionAssert.AreEqual(initialData, result.Buffer.ToArray());
        }

        [Test]
        public async Task ReadAsync_ByteAtATime_Success()
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes("Hello world");
            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));
            var testPipe = new Pipe();
            var r = new Base64PipeReader(testPipe.Reader);

            // Act
            var resultTask = r.ReadAsync();

            Assert.IsFalse(resultTask.IsCompleted);

            for (int i = 0; i < base64Data.Length; i++)
            {
                await testPipe.Writer.WriteAsync(base64Data.AsMemory(i, 1));
                await Task.Delay(10);
            }

            var result = await resultTask;

            // Assert
            Assert.AreEqual(3, result.Buffer.Length);

            r.AdvanceTo(result.Buffer.Start, result.Buffer.End);

            result = await r.ReadAsync();

            CollectionAssert.AreEqual(initialData, result.Buffer.ToArray());
        }

        [TestCase("")]
        [TestCase("f")]
        [TestCase("fo")]
        [TestCase("foo")]
        [TestCase("foob")]
        [TestCase("fooba")]
        [TestCase("foobar")]
        [TestCase("The quick brown fox jumps over the lazy dog")]
        public async Task ReadAsync_sdfsdf_Success(string text)
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes(text);

            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));

            Pipe testPipe = new Pipe();
            await testPipe.Writer.WriteAsync(base64Data).AsTask().DefaultTimeout();
            testPipe.Writer.Complete();

            Base64PipeReader r = new Base64PipeReader(testPipe.Reader);

            // Act
            var result = await r.ReadAsync().AsTask().DefaultTimeout();

            // Assert
            CollectionAssert.AreEqual(initialData, result.Buffer.ToArray());
        }

        [Test]
        public async Task ReadAsync_MultipleBase64Fragements_Success()
        {
            // Arrange
            var base64Data = Encoding.UTF8.GetBytes("AAAAAAYKBHRlc3Q=gAAAABBncnBjLXN0YXR1czogMA0K");

            Pipe testPipe = new Pipe();
            await testPipe.Writer.WriteAsync(base64Data).AsTask().DefaultTimeout();
            testPipe.Writer.Complete();

            Base64PipeReader r = new Base64PipeReader(testPipe.Reader);

            // Act 1
            var result1 = await r.ReadAsync().AsTask().DefaultTimeout();

            // Assert 1
            Assert.AreEqual("AAAAAAYKBHRlc3Q=", Convert.ToBase64String(result1.Buffer.ToArray()));

            // Act 2
            r.AdvanceTo(result1.Buffer.End);
            var result2 = await r.ReadAsync().AsTask().DefaultTimeout();

            // Assert 2
            Assert.AreEqual("gAAAABBncnBjLXN0YXR1czogMA0K", Convert.ToBase64String(result2.Buffer.ToArray()));

            // Act 3
            r.AdvanceTo(result2.Buffer.End);
            var result3 = await r.ReadAsync().AsTask().DefaultTimeout();

            // Assert 3
            Assert.IsTrue(result3.IsCompleted);
            Assert.AreEqual(0, result3.Buffer.Length);
        }
    }
}
