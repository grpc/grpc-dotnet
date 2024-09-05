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

using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests;

[TestFixture]
public class HttpContextServerCallContextTests
{
    [TestCase("127.0.0.1", 50051, "ipv4:127.0.0.1:50051")]
    [TestCase("::1", 50051, "ipv6:[::1]:50051")]
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
        var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>()!.Trailers;

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
        var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>()!.Trailers;

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
        var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>()!.Trailers;

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
        var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>()!.Trailers;

        Assert.AreEqual(2, responseTrailers.Count);
        Assert.AreEqual(StatusCode.OK.ToString("D"), responseTrailers[GrpcProtocolConstants.StatusTrailer]);
        Assert.AreEqual(Convert.ToBase64String(trailerBytes), responseTrailers["trailer-bin"]);
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
    [TestCase("9999999H", GrpcProtocolConstants.MaxDeadlineTicks)]
    [TestCase("99999999M", GrpcProtocolConstants.MaxDeadlineTicks)]
    [TestCase("99999999S", GrpcProtocolConstants.MaxDeadlineTicks)]
    public void Deadline_ParseValidHeader_ReturnDeadline(string header, long ticks)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = header;
        var context = CreateServerCallContext(httpContext);

        // Act
        context.Initialize(TestClock);

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
    public void Deadline_TooLong_LoggedAndMaximumDeadlineUsed()
    {
        // Arrange
        var testSink = new TestSink();
        var testLogger = new TestLogger(string.Empty, testSink, true);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "9999999H";
        var context = CreateServerCallContext(httpContext, testLogger);

        // Act
        context.Initialize(TestClock);

        // Assert
        Assert.AreEqual(TestClock.UtcNow.Add(TimeSpan.FromTicks(GrpcProtocolConstants.MaxDeadlineTicks)), context.Deadline);

        var write = testSink.Writes.Single(w => w.EventId.Name == "DeadlineTimeoutTooLong");
        Assert.AreEqual("Deadline timeout 416666.15:00:00 is above maximum allowed timeout of 99999999 seconds. Maximum timeout will be used.", write.State.ToString());
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

        var write = testSink.Writes.Single(w => w.EventId.Name == "DeadlineStarted");
        Assert.AreEqual("Request deadline timeout of 00:00:01 started.", write.State.ToString());

        write = testSink.Writes.Single(w => w.EventId.Name == "DeadlineExceeded");
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
        httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "100m";
        var context = CreateServerCallContext(httpContext, testLogger);
        context.Initialize();

        // Act
        await TestHelpers.AssertIsTrueRetryAsync(
            () => context.Status.StatusCode == StatusCode.DeadlineExceeded,
            "StatusCode not set to DeadlineExceeded.");

        // Assert
        var write = testSink.Writes.Single(w => w.EventId.Name == "DeadlineExceeded");
        Assert.AreEqual("Request with timeout of 00:00:00.1000000 has exceeded its deadline.", write.State.ToString());

        write = testSink.Writes.Single(w => w.EventId.Name == "DeadlineCancellationError");
        Assert.AreEqual("Error occurred while trying to cancel the request due to deadline exceeded.", write.State.ToString());
    }

    [Test]
    public void CancellationToken_WithDeadlineAndRequestAborted_DeadlineStatusNotSet()
    {
        // Arrange
        var testSink = new TestSink();
        var testLogger = new TestLogger(string.Empty, testSink, true);

        var requestLifetimeFeature = new TestHttpRequestLifetimeFeature();
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpRequestLifetimeFeature>(requestLifetimeFeature);
        httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "1000S";
        var context = CreateServerCallContext(httpContext, testLogger);
        context.Initialize();

        // Act
        requestLifetimeFeature.Abort();

        // Assert
        Assert.AreNotEqual(StatusCode.DeadlineExceeded, context.Status.StatusCode);
        Assert.IsTrue(context.CancellationToken.IsCancellationRequested);
    }

    [Test]
    public void CancellationToken_WithDeadlineAndRequestAborted_AccessCancellationTokenBeforeAbort_DeadlineStatusNotSet()
    {
        // Arrange
        var testSink = new TestSink();
        var testLogger = new TestLogger(string.Empty, testSink, true);

        var requestLifetimeFeature = new TestHttpRequestLifetimeFeature();
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpRequestLifetimeFeature>(requestLifetimeFeature);
        httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "1000S";
        var context = CreateServerCallContext(httpContext, testLogger);
        context.Initialize();

        // Act
        var ct = context.CancellationToken;
        requestLifetimeFeature.Abort();

        // Assert
        Assert.AreNotEqual(StatusCode.DeadlineExceeded, context.Status.StatusCode);
        Assert.IsTrue(ct.IsCancellationRequested);
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
        var certificate = GrpcProtocolHelpersTests.LoadCertificate(TestHelpers.ResolvePath(@"Certs/client.crt"));
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

    [TestCase("grpc-accept-encoding", false)]
    [TestCase("GRPC-ACCEPT-ENCODING", false)]
    [TestCase("grpc-encoding", false)]
    [TestCase("GRPC-ENCODING", false)]
    [TestCase("grpc-timeout", false)]
    [TestCase("GRPC-TIMEOUT", false)]
    [TestCase("content-type", false)]
    [TestCase("CONTENT-TYPE", false)]
    [TestCase("content-encoding", false)]
    [TestCase("CONTENT-ENCODING", false)]
    [TestCase("te", false)]
    [TestCase("TE", false)]
    [TestCase("host", false)]
    [TestCase("HOST", false)]
    [TestCase("accept-encoding", false)]
    [TestCase("ACCEPT-ENCODING", false)]
    [TestCase("user-agent", true)]
    [TestCase("USER-AGENT", true)]
    [TestCase("grpc-status-details-bin", true)]
    [TestCase("GRPC-STATUS-DETAILS-BIN", true)]
    public void RequestHeaders_ManyHttpRequestHeaders_HeadersFiltered(string headerName, bool addedToRequestHeaders)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // A base64 valid value is required for -bin headers
        var value = Convert.ToBase64String(Encoding.UTF8.GetBytes("Hello world"));
        httpContext.Request.Headers[headerName] = value;

        var serverCallContext = CreateServerCallContext(httpContext);

        // Act
        var headers = serverCallContext.RequestHeaders;

        // Assert
        var headerAdded = serverCallContext.RequestHeaders.Any(k => string.Equals(k.Key, headerName, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(addedToRequestHeaders, headerAdded);
    }

    [TestCase("HTTP/2", GrpcProtocolConstants.Http2ResetStreamCancel)]
    [TestCase("HTTP/3", GrpcProtocolConstants.Http3ResetStreamCancel)]
    public Task EndCallAsync_LongRunningDeadlineAbort_WaitsUntilDeadlineAbortIsFinished(
        string protocol,
        int expectedResetCode)
    {
        return LongRunningDeadline_WaitsUntilDeadlineIsFinished(
            nameof(HttpContextServerCallContext.EndCallAsync),
            context => context.EndCallAsync(),
            protocol,
            expectedResetCode);
    }

    [TestCase("HTTP/2", GrpcProtocolConstants.Http2ResetStreamCancel)]
    [TestCase("HTTP/3", GrpcProtocolConstants.Http3ResetStreamCancel)]
    public Task ProcessHandlerErrorAsync_LongRunningDeadlineAbort_WaitsUntilDeadlineAbortIsFinished(
        string protocol,
        int expectedResetCode)
    {
        return LongRunningDeadline_WaitsUntilDeadlineIsFinished(
            nameof(HttpContextServerCallContext.ProcessHandlerErrorAsync),
            context => context.ProcessHandlerErrorAsync(new Exception(), "Method!"),
            protocol,
            expectedResetCode);
    }

    private async Task LongRunningDeadline_WaitsUntilDeadlineIsFinished(
        string methodName,
        Func<HttpContextServerCallContext, Task> method,
        string protocol,
        int expectedResetCode)
    {
        // Arrange
        var syncPoint = new SyncPoint();

        var httpResetFeature = new TestHttpResetFeature();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Protocol = protocol;
        httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "200m";
        httpContext.Features.Set<IHttpResponseBodyFeature>(new TestBlockingHttpResponseCompletionFeature(syncPoint));
        httpContext.Features.Set<IHttpResetFeature>(httpResetFeature);

        var serverCallContext = CreateServerCallContext(httpContext);
        serverCallContext.Initialize();

        // Wait until CompleteAsync is called
        // That means we're inside the deadline method and the lock has been taken
        await syncPoint.WaitForSyncPoint().DefaultTimeout();

        // Act
        var methodTask = method(serverCallContext);

        // Assert
        if (await Task.WhenAny(methodTask, Task.Delay(TimeSpan.FromSeconds(0.2))).DefaultTimeout() == methodTask)
        {
            Assert.Fail($"{methodName} did not wait on lock taken by deadline cancellation.");
        }

        Assert.IsFalse(serverCallContext.DeadlineManager!.IsCallComplete);

        // Wait for dispose to finish
        syncPoint.Continue();
        await methodTask.DefaultTimeout();

        Assert.AreEqual(expectedResetCode, httpResetFeature.ErrorCode);

        Assert.IsTrue(serverCallContext.DeadlineManager!.IsCallComplete);
    }

    [Test]
    public async Task LongRunningDeadline_DisposeWaitsUntilFinished()
    {
        // Arrange
        var syncPoint = new SyncPoint();

        var httpResetFeature = new TestHttpResetFeature();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "200m";
        httpContext.Features.Set<IHttpResponseBodyFeature>(new TestBlockingHttpResponseCompletionFeature(syncPoint));
        httpContext.Features.Set<IHttpResetFeature>(httpResetFeature);

        var testSink = new TestSink();
        var testLogger = new TestLogger(string.Empty, testSink, true);

        var serverCallContext = CreateServerCallContext(httpContext, testLogger);
        serverCallContext.Initialize();

        // Wait until CompleteAsync is called
        // That means we're inside the deadline method and the lock has been taken
        await syncPoint.WaitForSyncPoint();

        // Act
        var disposeTask = serverCallContext.DeadlineManager!.DisposeAsync();

        // Assert
        Assert.IsFalse(disposeTask.IsCompletedSuccessfully);

        // Allow deadline exceeded to continue
        syncPoint.Continue();

        await disposeTask;
    }

    [Test]
    public void Initialize_MethodInPath_SetsMethodOnActivity()
    {
        using (new ActivityReplacer(GrpcServerConstants.HostActivityName))
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Package.Service/Method";
            var context = CreateServerCallContext(httpContext);

            // Act
            context.Initialize();

            // Assert
            Assert.AreEqual("/Package.Service/Method", Activity.Current!.Tags.Single(t => t.Key == GrpcServerConstants.ActivityMethodTag).Value);
        }
    }

    [Test]
    public void Initialize_MethodInPathAndChildActivity_SetsMethodOnActivity()
    {
        using (new ActivityReplacer(GrpcServerConstants.HostActivityName))
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Package.Service/Method";
            var context = CreateServerCallContext(httpContext);

            // Act
            using (new ActivityReplacer("ChildActivity"))
            {
                context.Initialize();
            }

            // Assert
            Assert.AreEqual("/Package.Service/Method", Activity.Current?.Tags.Single(t => t.Key == GrpcServerConstants.ActivityMethodTag).Value);
        }
    }

    [Test]
    public async Task EndCallAsync_StatusSet_SetsStatusOnActivity()
    {
        using (new ActivityReplacer(GrpcServerConstants.HostActivityName))
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            var context = CreateServerCallContext(httpContext);
            context.Status = new Status(StatusCode.ResourceExhausted, string.Empty);

            // Act
            context.Initialize();
            await context.EndCallAsync().DefaultTimeout();

            // Assert
            Assert.AreEqual("8", Activity.Current!.Tags.Single(t => t.Key == GrpcServerConstants.ActivityStatusCodeTag).Value);
        }
    }

    [Test]
    public async Task ProcessHandlerErrorAsync_Exception_SetsStatusOnActivity()
    {
        using (new ActivityReplacer(GrpcServerConstants.HostActivityName))
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            var context = CreateServerCallContext(httpContext);
            context.Status = new Status(StatusCode.ResourceExhausted, string.Empty);

            // Act
            context.Initialize();
            await context.ProcessHandlerErrorAsync(new Exception(), "MethodName");

            // Assert
            Assert.AreEqual("2", Activity.Current!.Tags.Single(t => t.Key == GrpcServerConstants.ActivityStatusCodeTag).Value);
        }
    }

    private HttpContextServerCallContext CreateServerCallContext(HttpContext httpContext, ILogger? logger = null)
    {
        return HttpContextServerCallContextHelper.CreateServerCallContext(
            httpContext: httpContext,
            logger: logger,
            initialize: false);
    }

    private class TestHttpResetFeature : IHttpResetFeature
    {
        public int? ErrorCode { get; private set; }

        public void Reset(int errorCode)
        {
            ErrorCode = errorCode;
        }
    }

    private class TestBlockingHttpResponseCompletionFeature : IHttpResponseBodyFeature
    {
        private readonly SyncPoint _syncPoint;

        public TestBlockingHttpResponseCompletionFeature(SyncPoint syncPoint)
        {
            _syncPoint = syncPoint;
        }

        public Stream Stream => throw new NotImplementedException();
        public PipeWriter Writer => throw new NotImplementedException();

        public Task CompleteAsync()
        {
            return _syncPoint.WaitToContinue();
        }

        public void DisableBuffering()
        {
            throw new NotImplementedException();
        }

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
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
}
