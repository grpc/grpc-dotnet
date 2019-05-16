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
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    public class FunctionalTestBase
    {
        private VerifyNoErrorsScope? _scope;

        protected GrpcTestFixture<FunctionalTestsWebsite.Startup> Fixture { get; private set; } = default!;

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
            _scope = new VerifyNoErrorsScope(Fixture.LoggerFactory, wrappedDisposable: null, expectedErrorsFilter: null);
        }

        [TearDown]
        public void TearDown()
        {
            // This will verify only expected errors were logged on the server for the previous test.
            _scope?.Dispose();
        }

        protected void SetExpectedErrorsFilter(Func<LogRecord, bool> expectedErrorsFilter)
        {
            _scope!.ExpectedErrorsFilter = expectedErrorsFilter;
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
