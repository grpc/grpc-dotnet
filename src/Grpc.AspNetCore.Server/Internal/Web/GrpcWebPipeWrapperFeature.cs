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

using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Grpc.AspNetCore.Server.Internal.Web
{
    internal class GrpcWebPipeWrapperFeature : IRequestBodyPipeFeature, IHttpResponseBodyFeature, IHttpResponseTrailersFeature
    {
        private readonly IHttpResponseBodyFeature _initialResponseFeature;
        private readonly Base64PipeReader _pipeReader;
        private readonly Base64PipeWriter _pipeWriter;
        private IHeaderDictionary _trailers;

        public GrpcWebPipeWrapperFeature(HttpContext httpContext)
        {
            var initialRequestFeature = httpContext.Features.Get<IRequestBodyPipeFeature>();
            _initialResponseFeature = httpContext.Features.Get<IHttpResponseBodyFeature>();

            _pipeReader = new Base64PipeReader(initialRequestFeature.Reader);
            _pipeWriter = new Base64PipeWriter(_initialResponseFeature.Writer);
            _trailers = new HeaderDictionary();
        }

        PipeReader IRequestBodyPipeFeature.Reader => _pipeReader;

        PipeWriter IHttpResponseBodyFeature.Writer => _pipeWriter;

        Stream IHttpResponseBodyFeature.Stream => _initialResponseFeature.Stream;

        IHeaderDictionary IHttpResponseTrailersFeature.Trailers
        {
            get => _trailers;
            set { _trailers = value; }
        }

        Task IHttpResponseBodyFeature.CompleteAsync() => _initialResponseFeature.CompleteAsync();

        void IHttpResponseBodyFeature.DisableBuffering() => _initialResponseFeature.DisableBuffering();

        Task IHttpResponseBodyFeature.SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken) =>
            _initialResponseFeature.SendFileAsync(path, offset, count, cancellationToken);

        Task IHttpResponseBodyFeature.StartAsync(CancellationToken cancellationToken) =>
            _initialResponseFeature.StartAsync(cancellationToken);
    }
}
