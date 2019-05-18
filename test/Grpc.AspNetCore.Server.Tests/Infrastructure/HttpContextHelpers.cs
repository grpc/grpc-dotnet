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

using Grpc.AspNetCore.Server.Features;
using Grpc.AspNetCore.Server.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading;

namespace Grpc.AspNetCore.Server.Tests.Infrastructure
{
    internal static class HttpContextHelpers
    {
        public static void SetupHttpContext(ServiceCollection services, CancellationToken? cancellationToken = null)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.RequestAborted = cancellationToken ?? CancellationToken.None;

            var serverCallContext = new HttpContextServerCallContext(httpContext, new GrpcServiceOptions(), NullLogger.Instance);
            httpContext.Features.Set<IServerCallContextFeature>(serverCallContext);

            services.AddSingleton<IHttpContextAccessor>(new TestHttpContextAccessor(httpContext));
        }
    }
}
