using System.Net;

namespace Grpc.Net.Client.Balancer.Internal;

internal sealed class HttpContentWrapper : HttpContent
{
    private readonly HttpContent _inner;
    private readonly Action _disposeAction;
    private bool _disposed;

    public HttpContentWrapper(HttpContent inner, Action disposeAction)
    {
        _inner = inner;
        _disposeAction = disposeAction;

        foreach (var kvp in inner.Headers)
        {
            Headers.TryAddWithoutValidation(kvp.Key, kvp.Value.ToArray());
        }
    }

#if NET5_0_OR_GREATER

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        using var content = _inner.ReadAsStream(cancellationToken);
        content.CopyTo(stream);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var content = await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (content.ConfigureAwait(false))
        {
            await content.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

#endif

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var content = await _inner.ReadAsStreamAsync().ConfigureAwait(false);
#if NET5_0_OR_GREATER
        await using (content.ConfigureAwait(false))
#else
        using (content)
#endif
        {
            await content.CopyToAsync(stream).ConfigureAwait(false);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && !_disposed)
        {
            _disposeAction();
            _inner.Dispose();
            _disposed = true;
        }
    }
}
