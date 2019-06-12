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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class HttpContextServerCallContextTests
    {
        [TestCase("127.0.0.1", 50051, "ipv4:127.0.0.1:50051")]
        [TestCase("::1", 50051, "ipv6:::1:50051")]
        public void Peer_FormatsRemoteAddressCorrectly(string ipAddress, int port, string expected)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);
            httpContext.Connection.RemotePort = port;

            // Act
            var serverCallContext = CreateServerCallContext(httpContext);

            // Assert
            Assert.AreEqual(expected, serverCallContext.Peer);
        }

        [Test]
        public async Task WriteResponseHeadersAsyncCore_AddsMetadataToResponseHeaders()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            var metadata = new Metadata();
            metadata.Add("foo", "bar");

            // Act
            var serverCallContext = CreateServerCallContext(httpContext);
            await serverCallContext.WriteResponseHeadersAsync(metadata);

            // Assert
            Assert.AreEqual("bar", httpContext.Response.Headers["foo"]);
        }

        [TestCase("foo-bin")]
        [TestCase("Foo-Bin")]
        [TestCase("FOO-BIN")]
        public async Task WriteResponseHeadersAsyncCore_Base64EncodesBinaryResponseHeaders(string headerName)
        {
            // Arrange
            var headerBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var httpContext = new DefaultHttpContext();
            var metadata = new Metadata();
            metadata.Add(headerName, headerBytes);

            // Act
            var serverCallContext = CreateServerCallContext(httpContext);
            await serverCallContext.WriteResponseHeadersAsync(metadata);

            // Assert
            CollectionAssert.AreEqual(headerBytes, Convert.FromBase64String(httpContext.Response.Headers["foo-bin"].ToString()));
        }

        [TestCase("name-suffix", "value", "name-suffix", "value")]
        [TestCase("Name-Suffix", "Value", "name-suffix", "Value")]
        public void RequestHeaders_LowercasesHeaderNames(string headerName, string headerValue, string expectedHeaderName, string expectedHeaderValue)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[headerName] = headerValue;

            // Act
            var serverCallContext = CreateServerCallContext(httpContext);

            // Assert
            Assert.AreEqual(1, serverCallContext.RequestHeaders.Count);
            var header = serverCallContext.RequestHeaders[0];
            Assert.AreEqual(expectedHeaderName, header.Key);
            Assert.AreEqual(expectedHeaderValue, header.Value);
        }

        [TestCase(":method")]
        [TestCase(":scheme")]
        [TestCase(":authority")]
        [TestCase(":path")]
        public void RequestHeaders_IgnoresPseudoHeaders(string headerName)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[headerName] = "dummy";

            // Act
            var serverCallContext = CreateServerCallContext(httpContext);

            // Assert
            Assert.AreEqual(0, serverCallContext.RequestHeaders.Count);
        }

        [TestCase("test-bin")]
        [TestCase("Test-Bin")]
        [TestCase("TEST-BIN")]
        public void RequestHeaders_ParsesBase64EncodedBinaryHeaders(string headerName)
        {
            var headerBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[headerName] = Convert.ToBase64String(headerBytes);

            // Act
            var serverCallContext = CreateServerCallContext(httpContext);

            // Assert
            Assert.AreEqual(1, serverCallContext.RequestHeaders.Count);
            var header = serverCallContext.RequestHeaders[0];
            Assert.True(header.IsBinary);
            CollectionAssert.AreEqual(headerBytes, header.ValueBytes);
        }

        [TestCase("a;b")]
        [TestCase("ZG9uZ")]
        public void RequestHeaders_ThrowsForNonBase64EncodedBinaryHeader(string header)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["test-bin"] = header;

            // Act
            var serverCallContext = CreateServerCallContext(httpContext);

            // Assert
            Assert.Throws<FormatException>(() => serverCallContext.RequestHeaders.Clear());
        }

        [TestCase("ZG9uZQ==")]
        [TestCase("AADQA7MnHnYTan7LCInL3K+EAfkpLdnnVVO1AgA=")]
        public void Base64Binaryheader_WithNoPadding(string base64)
        {
            // Arrange
            var testSink = new TestSink();
            var testLogger = new TestLogger(string.Empty, testSink, true);
            // strip the padding from base64, assuming that '='s are only at the end
            var headerValue = base64.Replace("=", "");

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["header-bin"] = headerValue;
            var context = CreateServerCallContext(httpContext, testLogger);

            // Act
            context.Initialize();

            // Assert
            Assert.IsTrue(context.RequestHeaders[0].ValueBytes.SequenceEqual(Convert.FromBase64String(base64)));
        }

        [TestCase("trailer-name", "trailer-value", "trailer-name", "trailer-value")]
        [TestCase("Trailer-Name", "Trailer-Value", "trailer-name", "Trailer-Value")]
        public void ConsolidateTrailers_LowercaseTrailerNames(string trailerName, string trailerValue, string expectedTrailerName, string expectedTrailerValue)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            httpContext.Features.Set<IHttpResponseFeature>(new TestHttpResponseFeature(hasStarted: true));
            var serverCallContext = CreateServerCallContext(httpContext);
            serverCallContext.ResponseTrailers.Add(trailerName, trailerValue);

            // Act
            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Assert
            var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>().Trailers;

            Assert.AreEqual(2, responseTrailers.Count);
            Assert.AreEqual(expectedTrailerValue, responseTrailers[expectedTrailerName].ToString());
            Assert.AreEqual("0", responseTrailers[GrpcProtocolConstants.StatusTrailer]);
        }

        [Test]
        public void ConsolidateTrailers_AppendsStatus_PercentEncodesMessage()
        {
            // Arrange
            var errorMessage = "\t\ntest with whitespace\r\nand Unicode BMP ☺ and non-BMP 😈\t\n";
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            httpContext.Features.Set<IHttpResponseFeature>(new TestHttpResponseFeature(hasStarted: true));
            var serverCallContext = CreateServerCallContext(httpContext);
            serverCallContext.Status = new Status(StatusCode.Internal, errorMessage);

            // Act
            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Assert
            var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>().Trailers;

            Assert.AreEqual(2, responseTrailers.Count);
            Assert.AreEqual(StatusCode.Internal.ToString("D"), responseTrailers[GrpcProtocolConstants.StatusTrailer]);
            Assert.AreEqual(PercentEncodingHelpers.PercentEncode(errorMessage), responseTrailers[GrpcProtocolConstants.MessageTrailer].ToString());
        }

        [Test]
        public void ConsolidateTrailers_ResponseNotStarted_ReturnTrailersInHeaders()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            httpContext.Features.Set<IHttpResponseFeature>(new TestHttpResponseFeature(hasStarted: false));
            var serverCallContext = CreateServerCallContext(httpContext);
            serverCallContext.Status = new Status(StatusCode.Internal, "Test message");

            // Act
            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Assert
            var headers = httpContext.Response.Headers;

            Assert.AreEqual(2, headers.Count);
            Assert.AreEqual(StatusCode.Internal.ToString("D"), headers[GrpcProtocolConstants.StatusTrailer]);
            Assert.AreEqual("Test message", headers[GrpcProtocolConstants.MessageTrailer].ToString());
        }

        [Test]
        public void ConsolidateTrailers_StatusOverwritesTrailers_PercentEncodesMessage()
        {
            // Arrange
            var errorMessage = "\t\ntest with whitespace\r\nand Unicode BMP ☺ and non-BMP 😈\t\n";
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            httpContext.Features.Set<IHttpResponseFeature>(new TestHttpResponseFeature(hasStarted: true));
            var serverCallContext = CreateServerCallContext(httpContext);
            serverCallContext.ResponseTrailers.Add(GrpcProtocolConstants.StatusTrailer, StatusCode.OK.ToString("D"));
            serverCallContext.ResponseTrailers.Add(GrpcProtocolConstants.MessageTrailer, "All is good");
            serverCallContext.Status = new Status(StatusCode.Internal, errorMessage);

            // Act
            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Assert
            var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>().Trailers;

            Assert.AreEqual(2, responseTrailers.Count);
            Assert.AreEqual(StatusCode.Internal.ToString("D"), responseTrailers[GrpcProtocolConstants.StatusTrailer]);
            Assert.AreEqual(PercentEncodingHelpers.PercentEncode(errorMessage), responseTrailers[GrpcProtocolConstants.MessageTrailer].ToString());
        }

        [TestCase("trailer-bin")]
        [TestCase("Trailer-Bin")]
        [TestCase("TRAILER-BIN")]
        public void ConsolidateTrailers_Base64EncodesBinaryTrailers(string trailerName)
        {
            // Arrange
            var trailerBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            httpContext.Features.Set<IHttpResponseFeature>(new TestHttpResponseFeature(hasStarted: true));
            var serverCallContext = CreateServerCallContext(httpContext);
            serverCallContext.ResponseTrailers.Add(trailerName, trailerBytes);

            // Act
            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Assert
            var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>().Trailers;

            Assert.AreEqual(2, responseTrailers.Count);
            Assert.AreEqual(StatusCode.OK.ToString("D"), responseTrailers[GrpcProtocolConstants.StatusTrailer]);
            Assert.AreEqual(Convert.ToBase64String(trailerBytes), responseTrailers["trailer-bin"]);
        }

        private class TestHttpResponseTrailersFeature : IHttpResponseTrailersFeature
        {
            public IHeaderDictionary Trailers { get; set; } = new HeaderDictionary();
        }

        private class TestHttpResponseFeature : HttpResponseFeature
        {
            private readonly bool _hasStarted;

            public TestHttpResponseFeature(bool hasStarted = false)
            {
                _hasStarted = hasStarted;
            }

            public override bool HasStarted => _hasStarted;
        }

        private static readonly ISystemClock TestClock = new TestSystemClock(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        private const long TicksPerMicrosecond = 10;
        private const long NanosecondsPerTick = 100;

        [Test]
        public void Deadline_NoTimeoutHeader_MaxValue()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            var context = CreateServerCallContext(httpContext);

            // Act
            context.Initialize();

            // Assert
            Assert.AreEqual(DateTime.MaxValue, context.Deadline);
        }

        [TestCase("1H", 1 * TimeSpan.TicksPerHour)]
        [TestCase("1M", 1 * TimeSpan.TicksPerMinute)]
        [TestCase("1S", 1 * TimeSpan.TicksPerSecond)]
        [TestCase("1m", 1 * TimeSpan.TicksPerMillisecond)]
        [TestCase("1u", 1 * TicksPerMicrosecond)]
        [TestCase("100H", 100 * TimeSpan.TicksPerHour)]
        [TestCase("100M", 100 * TimeSpan.TicksPerMinute)]
        [TestCase("100S", 100 * TimeSpan.TicksPerSecond)]
        [TestCase("100m", 100 * TimeSpan.TicksPerMillisecond)]
        [TestCase("100u", 100 * TicksPerMicrosecond)]
        [TestCase("100n", 100 / NanosecondsPerTick)]
        [TestCase("99999999m", 99999999 * TimeSpan.TicksPerMillisecond)]
        [TestCase("99999999u", 99999999 * TicksPerMicrosecond)]
        [TestCase("99999999n", 99999999 / NanosecondsPerTick)]
        public void Deadline_ParseValidHeader_ReturnDeadline(string header, long ticks)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = header;
            var context = CreateServerCallContext(httpContext);
            context.Clock = TestClock;

            // Act
            context.Initialize();

            // Assert
            Assert.AreEqual(TestClock.UtcNow.Add(TimeSpan.FromTicks(ticks)), context.Deadline);
        }

        [TestCase("0H")]
        [TestCase("0M")]
        [TestCase("0S")]
        [TestCase("0m")]
        [TestCase("0u")]
        [TestCase("0n")]
        [TestCase("-1M")]
        [TestCase("+1M")]
        [TestCase("99999999999999999999999999999M")]
        [TestCase("1.1M")]
        [TestCase(" 1M")]
        [TestCase("1M ")]
        [TestCase("1 M")]
        [TestCase("1,111M")]
        [TestCase("1")]
        [TestCase("M")]
        [TestCase("1G")]
        [TestCase("9999999H")] // too large for CancellationTokenSource
        [TestCase("99999999M")] // too large for CancellationTokenSource
        [TestCase("99999999S")] // too large for CancellationTokenSource
        public void Deadline_ParseInvalidHeader_IgnoresHeader(string header)
        {
            // Arrange
            var testSink = new TestSink();
            var testLogger = new TestLogger(string.Empty, testSink, true);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = header;
            var context = CreateServerCallContext(httpContext, testLogger);

            // Act
            context.Initialize();

            // Assert
            Assert.AreEqual(DateTime.MaxValue, context.Deadline);

            var write = testSink.Writes.Single(w => w.EventId.Name == "InvalidTimeoutIgnored");
            Assert.AreEqual($"Invalid grpc-timeout header value '{header}' has been ignored.", write.State.ToString());
        }

        [Test]
        public async Task CancellationToken_WithDeadline_CancellationRequested()
        {
            // Arrange
            var testSink = new TestSink();
            var testLogger = new TestLogger(string.Empty, testSink, true);

            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpRequestLifetimeFeature>(new TestHttpRequestLifetimeFeature());
            httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "1S";
            var context = CreateServerCallContext(httpContext, testLogger);
            context.Initialize();

            // Act
            try
            {
                await Task.WhenAll(
                    Task.Delay(int.MaxValue, context.CancellationToken),
                    Task.Delay(int.MaxValue, httpContext.RequestAborted)
                    ).DefaultTimeout();
                Assert.Fail();
            }
            catch (TaskCanceledException)
            {
            }

            // Assert
            Assert.IsTrue(context.CancellationToken.IsCancellationRequested);
            Assert.IsTrue(httpContext.RequestAborted.IsCancellationRequested);

            var write = testSink.Writes.Single(w => w.EventId.Name == "DeadlineExceeded");
            Assert.AreEqual("Request with timeout of 00:00:01 has exceeded its deadline.", write.State.ToString());
        }

        [Test]
        public async Task CancellationToken_WithDeadlineAndNoLifetimeFeature_ErrorLogged()
        {
            // Arrange
            var testSink = new TestSink();
            var testLogger = new TestLogger(string.Empty, testSink, true);

            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpRequestLifetimeFeature>(new TestHttpRequestLifetimeFeature(throwError: true));
            httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "1S";
            var context = CreateServerCallContext(httpContext, testLogger);
            context.Initialize();

            // Act
            while (context.Status.StatusCode != StatusCode.DeadlineExceeded)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            // Assert
            Assert.IsFalse(context.CancellationToken.IsCancellationRequested);

            var write = testSink.Writes.Single(w => w.EventId.Name == "DeadlineExceeded");
            Assert.AreEqual("Request with timeout of 00:00:01 has exceeded its deadline.", write.State.ToString());

            write = testSink.Writes.Single(w => w.EventId.Name == "DeadlineCancellationError");
            Assert.AreEqual("Error occurred while trying to cancel the request due to deadline exceeded.", write.State.ToString());
        }

        [Test]
        public void AuthContext_NoClientCertificate_Unauthenticated()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            var serverCallContext = CreateServerCallContext(httpContext);

            // Act
            var authContext = serverCallContext.AuthContext;

            // Assert
            Assert.AreEqual(false, authContext.IsPeerAuthenticated);
        }

        [Test]
        public void AuthContext_HasClientCertificate_Authenticated()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            var certificate = new X509Certificate2(TestHelpers.ResolvePath(@"Certs/client.crt"));
            httpContext.Connection.ClientCertificate = certificate;
            var serverCallContext = CreateServerCallContext(httpContext);

            // Act
            var authContext = serverCallContext.AuthContext;

            // Assert
            Assert.AreEqual(true, authContext.IsPeerAuthenticated);
        }

        [Test]
        public void UserState_AddState_AddedToHttpContextItems()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            var serverCallContext = CreateServerCallContext(httpContext);

            // Act
            serverCallContext.UserState["TestKey"] = "TestValue";

            // Assert
            Assert.AreEqual("TestValue", serverCallContext.UserState["TestKey"]);
            Assert.AreEqual("TestValue", httpContext.Items["TestKey"]);
        }

        [TestCase(GrpcProtocolConstants.MessageAcceptEncodingHeader, false)]
        [TestCase(GrpcProtocolConstants.MessageEncodingHeader, false)]
        [TestCase(GrpcProtocolConstants.TimeoutHeader, false)]
        [TestCase("content-type", false)]
        [TestCase("te", false)]
        [TestCase("host", false)]
        [TestCase("accept-encoding", false)]
        [TestCase("user-agent", true)]
        public void RequestHeaders_ManyHttpRequestHeaders_HeadersFiltered(string headerName, bool addedToRequestHeaders)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            var httpRequest = new DefaultHttpRequest(httpContext);
            httpRequest.Headers[headerName] = "value";
            var serverCallContext = CreateServerCallContext(httpContext);

            // Act
            var headers = serverCallContext.RequestHeaders;

            // Assert
            var headerAdded = serverCallContext.RequestHeaders.Any(k => string.Equals(k.Key, headerName, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(addedToRequestHeaders, headerAdded);
        }

        [Test]
        public async Task Dispose_LongRunningDeadlineAbort_WaitsUntilDeadlineAbortIsFinished()
        {
            // Arrange
            var blockingLifeTimeFeature = new TestBlockingHttpRequestLifetimeFeature();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "100n";
            httpContext.Features.Set<IHttpRequestLifetimeFeature>(blockingLifeTimeFeature);

            var testSink = new TestSink();
            var testLogger = new TestLogger(string.Empty, testSink, true);

            var serverCallContext = CreateServerCallContext(httpContext, testLogger);
            serverCallContext.Initialize();

            // Wait until we're inside the deadline method and the lock has been taken
            while (true)
            {
                if (testSink.Writes.Any(w => w.EventId.Name == "DeadlineExceeded"))
                {
                    break;
                }
            }

            // Act
            var disposeTask = Task.Run(() => serverCallContext.Dispose());

            // Assert
            if (await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(0.2))) == disposeTask)
            {
                Assert.Fail("Dispose did not wait on lock taken by deadline cancellation.");
            }

            Assert.IsFalse(serverCallContext._disposed);

            // Wait for dispose to finish
            blockingLifeTimeFeature.CancelBlocking();
            await disposeTask.DefaultTimeout();

            Assert.IsTrue(serverCallContext._disposed);
        }

        [Test]
        public void DeadlineTimer_ExecutedAfterDispose_RequestNotAborted()
        {
            // Arrange
            var lifetimeFeature = new TestHttpRequestLifetimeFeature();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "1H";
            httpContext.Features.Set<IHttpRequestLifetimeFeature>(lifetimeFeature);

            var serverCallContext = CreateServerCallContext(httpContext);
            serverCallContext.Initialize();
            serverCallContext.Dispose();

            // Act
            serverCallContext.DeadlineExceeded(TimeSpan.Zero);

            // Assert
            Assert.IsFalse(lifetimeFeature.RequestAborted.IsCancellationRequested);
        }

        private HttpContextServerCallContext CreateServerCallContext(HttpContext httpContext, ILogger? logger = null)
        {
            return new HttpContextServerCallContext(httpContext, new GrpcServiceOptions(), logger ?? NullLogger.Instance);
        }

        private class TestBlockingHttpRequestLifetimeFeature : IHttpRequestLifetimeFeature
        {
            private readonly CancellationTokenSource _cts;

            public TestBlockingHttpRequestLifetimeFeature()
            {
                _cts = new CancellationTokenSource();
            }

            public CancellationToken RequestAborted
            {
                get => _cts.Token;
                set => throw new NotSupportedException();
            }

            public void Abort()
            {
                _cts.Token.WaitHandle.WaitOne();
            }

            public void CancelBlocking()
            {
                _cts.Cancel();
            }
        }

        private class TestHttpRequestLifetimeFeature : IHttpRequestLifetimeFeature
        {
            private readonly CancellationTokenSource _cts;
            private readonly bool _throwError;

            public TestHttpRequestLifetimeFeature(bool throwError = false)
            {
                _cts = new CancellationTokenSource();
                _throwError = throwError;
            }

            public CancellationToken RequestAborted
            {
                get => _cts.Token;
                set => throw new NotSupportedException();
            }

            public void Abort()
            {
                if (_throwError)
                {
                    throw new Exception("Error thrown.");
                }

                _cts.Cancel();
            }
        }

        private class TestSystemClock : ISystemClock
        {
            public TestSystemClock(DateTime utcNow)
            {
                UtcNow = utcNow;
            }

            public DateTime UtcNow { get; }
        }
    }
}
