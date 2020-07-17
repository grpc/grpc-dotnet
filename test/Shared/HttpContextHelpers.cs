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
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.Tests.Shared
{
    internal static class HttpContextHelpers
    {
        public static void SetupHttpContext(ServiceCollection services, CancellationToken? cancellationToken = null)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.RequestAborted = cancellationToken ?? CancellationToken.None;

            services.AddSingleton<IHttpContextAccessor>(new TestHttpContextAccessor(httpContext));
        }

        public static HttpContext CreateContext(
            bool isMaxRequestBodySizeFeatureReadOnly = false,
            bool skipTrailerFeatureSet = false,
            string? protocol = null,
            string? contentType = null,
            IServiceProvider? serviceProvider = null)
        {
            var httpContext = new DefaultHttpContext();
            var responseFeature = new TestHttpResponseFeature();
            var responseBodyFeature = new TestHttpResponseBodyFeature(httpContext.Features.Get<IHttpResponseBodyFeature>(), responseFeature);

            httpContext.RequestServices = serviceProvider!;
            httpContext.Request.Protocol = protocol ?? GrpcProtocolConstants.Http2Protocol;
            httpContext.Request.ContentType = contentType ?? GrpcProtocolConstants.GrpcContentType;
            httpContext.Features.Set<IHttpMinRequestBodyDataRateFeature>(new TestMinRequestBodyDataRateFeature());
            httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(new TestMaxRequestBodySizeFeature(isMaxRequestBodySizeFeatureReadOnly, 100));
            httpContext.Features.Set<IHttpResponseFeature>(responseFeature);
            httpContext.Features.Set<IHttpResponseBodyFeature>(responseBodyFeature);
            if (!skipTrailerFeatureSet)
            {
                httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            }

            return httpContext;
        }

        public class TestHttpResponseFeature : IHttpResponseFeature
        {
            public Stream Body { get; set; }
            public bool HasStarted { get; internal set; }
            public IHeaderDictionary Headers { get; set; }
            public string? ReasonPhrase { get; set; }
            public int StatusCode { get; set; }

            public TestHttpResponseFeature()
            {
                StatusCode = 200;
                Headers = new HeaderDictionary();
                Body = Stream.Null;
            }

            public void OnCompleted(Func<object, Task> callback, object state)
            {
            }

            public void OnStarting(Func<object, Task> callback, object state)
            {
                HasStarted = true;
            }
        }

        public class TestHttpResponseBodyFeature : IHttpResponseBodyFeature
        {
            private readonly IHttpResponseBodyFeature _innerResponseBodyFeature;
            private readonly TestHttpResponseFeature _responseFeature;

            public Stream Stream => _innerResponseBodyFeature.Stream;
            public PipeWriter Writer => _innerResponseBodyFeature.Writer;

            public TestHttpResponseBodyFeature(IHttpResponseBodyFeature innerResponseBodyFeature, TestHttpResponseFeature responseFeature)
            {
                _innerResponseBodyFeature = innerResponseBodyFeature ?? throw new ArgumentNullException(nameof(innerResponseBodyFeature));
                _responseFeature = responseFeature ?? throw new ArgumentNullException(nameof(responseFeature));
            }

            public Task CompleteAsync()
            {
                return _innerResponseBodyFeature.CompleteAsync();
            }

            public void DisableBuffering()
            {
                _innerResponseBodyFeature.DisableBuffering();
            }

            public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
            {
                return _innerResponseBodyFeature.SendFileAsync(path, offset, count, cancellationToken);
            }

            public Task StartAsync(CancellationToken cancellationToken = default)
            {
                _responseFeature.HasStarted = true;
                return _innerResponseBodyFeature.StartAsync(cancellationToken);
            }
        }

        public class TestMinRequestBodyDataRateFeature : IHttpMinRequestBodyDataRateFeature
        {
            public MinDataRate MinDataRate { get; set; } = new MinDataRate(1, TimeSpan.FromSeconds(5));
        }

        public class TestMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
        {
            public TestMaxRequestBodySizeFeature(bool isReadOnly, long? maxRequestBodySize)
            {
                IsReadOnly = isReadOnly;
                MaxRequestBodySize = maxRequestBodySize;
            }

            public bool IsReadOnly { get; }
            public long? MaxRequestBodySize { get; set; }
        }
    }
}
