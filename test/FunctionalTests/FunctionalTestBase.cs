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

using System.Globalization;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests;

public class FunctionalTestBase
{
    private GrpcTestContext? _testContext;
    private GrpcChannel? _channel;

    protected GrpcTestFixture<FunctionalTestsWebsite.Startup> Fixture { get; private set; } = default!;

    protected ILoggerFactory LoggerFactory => _testContext!.LoggerFactory;

    protected ILogger Logger => _testContext!.Logger;

    protected GrpcChannel Channel => _channel ??= CreateChannel();

    protected GrpcChannel CreateChannel(bool useHandler = false, ServiceConfig? serviceConfig = null,
        int? maxRetryAttempts = null, long? maxRetryBufferSize = null, long? maxRetryBufferPerCallSize = null,
        int? maxReceiveMessageSize = null, bool? throwOperationCanceledOnCancellation = null)
    {
        var options = new GrpcChannelOptions
        {
            LoggerFactory = LoggerFactory,
            ServiceConfig = serviceConfig,
            ThrowOperationCanceledOnCancellation = throwOperationCanceledOnCancellation ?? false
        };
        // Don't overwrite defaults
        if (maxRetryAttempts != null)
        {
            options.MaxRetryAttempts = maxRetryAttempts;
        }
        if (maxRetryBufferSize != null)
        {
            options.MaxRetryBufferSize = maxRetryBufferSize;
        }
        if (maxRetryBufferPerCallSize != null)
        {
            options.MaxRetryBufferPerCallSize = maxRetryBufferPerCallSize;
        }
        if (useHandler)
        {
            options.HttpHandler = Fixture.Handler;
        }
        else
        {
            options.HttpClient = Fixture.Client;
        }
        if (maxReceiveMessageSize != null)
        {
            options.MaxReceiveMessageSize = maxReceiveMessageSize;
        }
        return GrpcChannel.ForAddress(Fixture.Client.BaseAddress!, options);
    }

    protected virtual void ConfigureServices(IServiceCollection services) { }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        ServerRetryHelper.BindPortsWithRetry(port =>
        {
            Fixture = new GrpcTestFixture<FunctionalTestsWebsite.Startup>(
                ConfigureServices,
            addConfiguration: configuration =>
            {
                // An explicit (non-dynamic port) is required for HTTP/3 because the port will be shared
                // between TCP and UDP. Dynamic ports don't support binding to both transports at the same time.
                configuration["Http3Port"] = port.ToString(CultureInfo.InvariantCulture);
            });
        }, NullLogger.Instance);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        Fixture.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        _testContext = new GrpcTestContext();
        Fixture.ServerLogged += _testContext.ServerFixtureOnServerLogged;
    }

    [TearDown]
    public void TearDown()
    {
        if (_testContext != null)
        {
            Fixture.ServerLogged -= _testContext.ServerFixtureOnServerLogged;
            _testContext.Dispose();
        }

        _channel = null;
    }

    public IList<LogRecord> Logs => _testContext!.Scope.Logs;

    public void ClearLogs() => _testContext!.Scope.ClearLogs();

    protected void AssertHasLogRpcConnectionError(StatusCode statusCode, string detail)
    {
        AssertHasLog(LogLevel.Information, "RpcConnectionError", $"Error status code '{statusCode}' with detail '{detail}' raised.");
    }

    protected void AssertHasLog(LogLevel logLevel, string name, string? message = null, Func<Exception, bool>? exceptionMatch = null)
    {
        if (HasLog(logLevel, name, message, exceptionMatch))
        {
            return;
        }

        Assert.Fail($"No match. Log level = {logLevel}, name = {name}, message = '{message ?? "(null)"}'.");
    }

    protected bool HasLog(LogLevel logLevel, string name, string? message = null, Func<Exception, bool>? exceptionMatch = null)
    {
        return Logs.Any(r =>
        {
            var match = r.LogLevel == logLevel && r.EventId.Name == name && (r.Message == message || message == null);
            if (exceptionMatch != null)
            {
                match = match && r.Exception != null && exceptionMatch(r.Exception);
            }
            return match;
        });
    }

    protected bool HasLogException(Func<Exception, bool> exceptionMatch)
    {
        return Logs.Any(x => x.Exception != null && exceptionMatch(x.Exception));
    }

    protected void SetExpectedErrorsFilter(Func<LogRecord, bool> expectedErrorsFilter)
    {
        _testContext!.Scope.ExpectedErrorsFilter = expectedErrorsFilter;
    }

    protected static string? GetRpcExceptionDetail(Exception? ex)
    {
        if (ex is RpcException rpcException)
        {
            return rpcException.Status.Detail;
        }

        return null;
    }

    protected static bool IsWriteCanceledException(Exception ex)
    {
        return ex is InvalidOperationException ||
            ex is IOException ||
            ex is OperationCanceledException;
    }
}
