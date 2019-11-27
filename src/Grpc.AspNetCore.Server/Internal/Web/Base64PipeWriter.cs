using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Server.Internal.Web
{
    /// <summary>
    /// Writes bytes as base64 encoded to the inner writer.
    /// </summary>
    internal class Base64PipeWriter : PipeWriter
    {
        private readonly PipeWriter _inner;
        private int _remainder;

        public Base64PipeWriter(PipeWriter inner)
        {
            _inner = inner;
        }

        public override void Advance(int bytes)
        {
            if (bytes == 0)
            {
                return;
            }

            var resolvedBytes = bytes + _remainder;
            var newRemainder = resolvedBytes % 3;
            var bytesToProcess = resolvedBytes - newRemainder;

            if (bytesToProcess > 0)
            {
                // When writing base64 content we don't want any padding until the end.
                // Process data in intervals of 3, and save the remainder at the start of a new span
                var buffer = _inner.GetSpan((bytesToProcess / 3) * 4);

                PreserveRemainder(newRemainder, buffer.Slice(bytesToProcess), out var b1, out var b2, out var b3);

                CoreAdvance(bytesToProcess, buffer);

                SetRemainder(newRemainder, b1, b2, b3);
            }
            else
            {
                _remainder = _remainder += bytes;
            }
        }

        private void SetRemainder(int newRemainder, byte b1, byte b2, byte b3)
        {
            if (newRemainder >= 1)
            {
                var buffer = _inner.GetSpan(newRemainder);
                buffer[0] = b1;
                if (newRemainder >= 2)
                {
                    buffer[1] = b2;
                    if (newRemainder >= 3)
                    {
                        buffer[2] = b3;
                    }
                }
            }
        }

        private void CoreAdvance(int bytesToProcess, Span<byte> buffer)
        {
            var status = Base64.EncodeToUtf8InPlace(buffer, bytesToProcess, out var bytesWritten);
            if (status != OperationStatus.Done)
            {
                throw new InvalidOperationException($"Expected status of Done when converting to base64. Got {status}.");
            }

            _inner.Advance(bytesWritten);
        }

        private void PreserveRemainder(int newRemainder, Span<byte> buffer, out byte b1, out byte b2, out byte b3)
        {
            if (newRemainder >= 1)
            {
                b1 = buffer[0];

                if (newRemainder >= 2)
                {
                    b2 = buffer[1];

                    if (newRemainder >= 3)
                    {
                        b3 = buffer[2];
                        _remainder = 3;
                    }
                    else
                    {
                        _remainder = 2;
                        b3 = 0;
                    }
                }
                else
                {
                    _remainder = 1;
                    b2 = 0;
                    b3 = 0;
                }
            }
            else
            {
                _remainder = 0;
                b1 = 0;
                b2 = 0;
                b3 = 0;
            }
        }

        public override void CancelPendingFlush()
        {
            _inner.CancelPendingFlush();
        }

        public override void Complete(Exception? exception = null)
        {
            if (exception == null)
            {
                WriteRemainder();
            }

            _inner.Complete(exception);
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            WriteRemainder();

            return _inner.FlushAsync(cancellationToken);
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            // Get size plus the current remainder (it is included at the start of the data returned)
            return _inner.GetMemory(sizeHint + _remainder).Slice(_remainder);
        }

        public override Span<byte> GetSpan(int sizeHint = 0)
        {
            // Get size plus the current remainder (it is included at the start of the data returned)
            return _inner.GetSpan(sizeHint + _remainder).Slice(_remainder);
        }

        private void WriteRemainder()
        {
            // Write remaining data. Padding is automatically added.
            if (_remainder > 0)
            {
                var buffer = _inner.GetSpan(4);
                CoreAdvance(_remainder, buffer);

                _remainder = 0;
            }
        }
    }
}
