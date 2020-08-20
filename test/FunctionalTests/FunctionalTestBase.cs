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
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    public class FunctionalTestBase
    {
        private GrpcTestContext? _testContext;
        private GrpcChannel? _channel;

        protected GrpcTestFixture<FunctionalTestsWebsite.Startup> Fixture { get; private set; } = default!;

        protected ILoggerFactory LoggerFactory => _testContext!.LoggerFactory;

        protected ILogger Logger => _testContext!.Logger;

        protected GrpcChannel Channel => _channel ??= CreateChannel();

        protected GrpcChannel CreateChannel(bool useHandler = false)
        {
            var options = new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory
            };
            if (useHandler)
            {
                options.HttpHandler = Fixture.Handler;
            }
            else
            {
                options.HttpClient = Fixture.Client;
            }
            return GrpcChannel.ForAddress(Fixture.Client.BaseAddress!, options);
        }

        protected virtual void ConfigureServices(IServiceCollection services) { }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Fixture = new GrpcTestFixture<FunctionalTestsWebsite.Startup>(ConfigureServices);
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
            AssertHasLog(LogLevel.Information, "RpcConnectionError", $"Error status code '{statusCode}' raised.", e => GetRpcExceptionDetail(e) == detail);
        }

        protected void AssertHasLog(LogLevel logLevel, string name, string message, Func<Exception, bool>? exceptionMatch = null)
        {
            if (HasLog(logLevel, name, message, exceptionMatch))
            {
                return;
            }

            Assert.Fail($"No match. Log level = {logLevel}, name = {name}, message = '{message}'.");
        }

        protected bool HasLog(LogLevel logLevel, string name, string message, Func<Exception, bool>? exceptionMatch = null)
        {
            return Logs.Any(r =>
            {
                var match = r.LogLevel == logLevel && r.EventId.Name == name && r.Message == message;
                if (exceptionMatch != null)
                {
                    match = match && r.Exception != null && exceptionMatch(r.Exception);
                }
                return match;
            });
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
    }
}
