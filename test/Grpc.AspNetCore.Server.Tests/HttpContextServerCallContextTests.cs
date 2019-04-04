﻿#region Copyright notice and license

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
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
        public void ConsolidateTrailers_AppendsStatus()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            var serverCallContext = CreateServerCallContext(httpContext);
            serverCallContext.Status = new Status(StatusCode.Internal, "Error message");

            // Act
            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Assert
            var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>().Trailers;

            Assert.AreEqual(2, responseTrailers.Count);
            Assert.AreEqual(StatusCode.Internal.ToString("D"), responseTrailers[GrpcProtocolConstants.StatusTrailer]);
            Assert.AreEqual("Error message", responseTrailers[GrpcProtocolConstants.MessageTrailer]);
        }

        [Test]
        public void ConsolidateTrailers_StatusOverwritesTrailers()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            var serverCallContext = CreateServerCallContext(httpContext);
            serverCallContext.ResponseTrailers.Add(GrpcProtocolConstants.StatusTrailer, StatusCode.OK.ToString("D"));
            serverCallContext.ResponseTrailers.Add(GrpcProtocolConstants.MessageTrailer, "All is good");
            serverCallContext.Status = new Status(StatusCode.Internal, "Error message");

            // Act
            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Assert
            var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>().Trailers;

            Assert.AreEqual(2, responseTrailers.Count);
            Assert.AreEqual(StatusCode.Internal.ToString("D"), responseTrailers[GrpcProtocolConstants.StatusTrailer]);
            Assert.AreEqual("Error message", responseTrailers[GrpcProtocolConstants.MessageTrailer]);
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

        private HttpContextServerCallContext CreateServerCallContext(HttpContext httpContext, ILogger logger = null)
        {
            return new HttpContextServerCallContext(httpContext, new GrpcServiceOptions(), logger ?? NullLogger.Instance);
        }

        private class TestHttpRequestLifetimeFeature : IHttpRequestLifetimeFeature
        {
            private readonly CancellationTokenSource _cts;

            public TestHttpRequestLifetimeFeature()
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
