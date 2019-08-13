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
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        protected GrpcChannel Channel
        {
            get
            {
                if (_channel == null)
                {
                    _channel = GrpcChannel.ForAddress(Fixture.Client.BaseAddress, new GrpcChannelOptions
                    {
                        LoggerFactory = LoggerFactory,
                        HttpClient = Fixture.Client
                    });
                }

                return _channel;
            }
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
