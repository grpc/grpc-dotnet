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

using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Grpc.AspNetCore.Web.Internal;

internal sealed class GrpcWebFeature :
    IRequestBodyPipeFeature,
    IHttpResponseBodyFeature,
    IHttpResponseTrailersFeature,
    IHttpResetFeature
{
    private readonly IHttpResponseBodyFeature _initialResponseFeature;
    private readonly IRequestBodyPipeFeature? _initialRequestFeature;
    private readonly IHttpResetFeature? _initialResetFeature;
    private readonly IHttpResponseTrailersFeature? _initialTrailersFeature;
    private Stream? _responseStream;
    private bool _isComplete;

    public GrpcWebFeature(ServerGrpcWebContext grcpWebContext, HttpContext httpContext)
    {
        // Capture existing features. We'll use these internally, and restore them onto the context
        // once the middleware has finished executing.

        // Note that some of these will be missing depending on the host and protocol.
        // e.g.
        // - IHttpResponseTrailersFeature and IHttpResetFeature will be missing when HTTP/1.1.
        // - IRequestBodyPipeFeature will be missing when in IIS.
        _initialRequestFeature = httpContext.Features.Get<IRequestBodyPipeFeature>();
        _initialResponseFeature = GetRequiredFeature<IHttpResponseBodyFeature>(httpContext);
        _initialResetFeature = httpContext.Features.Get<IHttpResetFeature>();
        _initialTrailersFeature = httpContext.Features.Get<IHttpResponseTrailersFeature>();

        var innerReader = _initialRequestFeature?.Reader ?? httpContext.Request.BodyReader;
        var innerWriter = _initialResponseFeature.Writer ?? httpContext.Response.BodyWriter;

        Trailers = new HeaderDictionary();
        if (grcpWebContext.Request == ServerGrpcWebMode.GrpcWebText)
        {
            Reader = new Base64PipeReader(innerReader);
        }
        else
        {
            Reader = innerReader;
        }
        if (grcpWebContext.Response == ServerGrpcWebMode.GrpcWebText)
        {
            Writer = new Base64PipeWriter(innerWriter);
        }
        else
        {
            Writer = innerWriter;
        }

        httpContext.Features.Set<IRequestBodyPipeFeature>(this);
        httpContext.Features.Set<IHttpResponseBodyFeature>(this);
        httpContext.Features.Set<IHttpResponseTrailersFeature>(this);
        httpContext.Features.Set<IHttpResetFeature>(this);
    }

    private static T GetRequiredFeature<T>(HttpContext httpContext)
    {
        var feature = httpContext.Features.Get<T>();
        if (feature == null)
        {
            throw new InvalidOperationException($"Couldn't get {typeof(T).FullName} from the current request.");
        }

        return feature;
    }

    public PipeReader Reader { get; }

    public PipeWriter Writer { get; }

    Stream IHttpResponseBodyFeature.Stream => _responseStream ??= Writer.AsStream();

    public IHeaderDictionary Trailers { get; set; }

    public async Task CompleteAsync()
    {
        // TODO(JamesNK): When CompleteAsync is called from another thread (e.g. deadline exceeded),
        // there is the potential for the main thread and CompleteAsync to both be writing to the response.
        // Shouldn't matter to the client because it will have already thrown deadline exceeded error, but
        // the response could return badly formatted trailers.
        await WriteTrailersAsync();
        await _initialResponseFeature.CompleteAsync();
        _isComplete = true;
    }

    public void DisableBuffering() => _initialResponseFeature.DisableBuffering();

    public void Reset(int errorCode)
    {
        // We set a reset feature so that HTTP/1.1 doesn't call HttpContext.Abort() on deadline.
        // In HTTP/1.1 this will do nothing. In HTTP/2+ it will call the real reset feature.
        _initialResetFeature?.Reset(errorCode);
    }

    internal void DetachFromContext(HttpContext httpContext)
    {
        httpContext.Features.Set<IRequestBodyPipeFeature>(_initialRequestFeature!);
        httpContext.Features.Set<IHttpResponseBodyFeature>(_initialResponseFeature);
        httpContext.Features.Set<IHttpResponseTrailersFeature>(_initialTrailersFeature!);
        httpContext.Features.Set<IHttpResetFeature>(_initialResetFeature!);
    }

    public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Sending a file during a gRPC call is not supported.");

    public Task StartAsync(CancellationToken cancellationToken) =>
        _initialResponseFeature.StartAsync(cancellationToken);

    public Task WriteTrailersAsync()
    {
        if (!_isComplete && Trailers.Count > 0)
        {
            return GrpcWebProtocolHelpers.WriteTrailersAsync(Trailers, Writer);
        }

        return Task.CompletedTask;
    }
}
