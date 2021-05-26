using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HttpPipe
{
    public abstract class WriteClosableStream : Stream
    {
        public abstract bool CanCloseWrite { get; }

        public abstract void CloseWrite();
    }

    public interface IPeekableStream
    {
        /// <summary>
        /// Peek the underlying stream, can be used in order to avoid a blocking read call when no data is available
        /// https://stackoverflow.com/questions/6846365/check-for-eof-in-namedpipeclientstream
        /// https://msdn.microsoft.com/en-us/library/windows/desktop/aa365779(v=vs.85).aspx
        /// </summary>
        /// <param name="buffer">buffer to put peeked data in</param>
        /// <param name="toPeek">max number of bytes to peek</param>
        /// <param name="peeked">number of bytes that were peeked</param>
        /// <param name="available">number of bytes that were available for peeking</param>
        /// <param name="remaining">number of available bytes minus number of peeked</param>
        /// <returns>whether peek operation succeeded</returns>
        bool Peek(byte[] buffer, uint toPeek, out uint peeked, out uint available, out uint remaining);
    }

    public class ContainerDStream : WriteClosableStream, IPeekableStream
    {
        private readonly PipeStream _stream;
        private readonly EventWaitHandle _event = new EventWaitHandle(false, EventResetMode.AutoReset);

        public ContainerDStream(PipeStream stream)
        {
            _stream = stream;
            //_stream.ReadMode = PipeTransmissionMode.Message;
        }

        public override bool CanCloseWrite => true;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _stream.ReadAsync(buffer, default);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _stream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _stream.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Console.WriteLine($"client still connected: {_stream.IsConnected}");
            Console.WriteLine($"number of server instances: {((NamedPipeClientStream)_stream).NumberOfServerInstances}");
            Console.WriteLine($"client transmission mode: {_stream.TransmissionMode}");
            Console.WriteLine($"client read mode: {_stream.ReadMode}");
            Console.WriteLine($"InBufferSize is: {_stream.InBufferSize}");
            Console.WriteLine($"OutBufferSize is: {_stream.OutBufferSize}");
            
            return _stream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        [DllImport("api-ms-win-core-file-l1-1-0.dll", SetLastError = true)]
        private static extern int WriteFile(SafeHandle handle, IntPtr buffer, int numBytesToWrite, IntPtr numBytesWritten, ref NativeOverlapped overlapped);

        [DllImport("api-ms-win-core-io-l1-1-0.dll", SetLastError = true)]
        private static extern int GetOverlappedResult(SafeHandle handle, ref NativeOverlapped overlapped, out int numBytesWritten, int wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PeekNamedPipe(SafeHandle handle, byte[] buffer, uint nBufferSize, ref uint bytesRead, ref uint bytesAvail, ref uint BytesLeftThisMessage);

        public override void CloseWrite()
        {
            // The Docker daemon expects a write of zero bytes to signal the end of writes. Use native
            // calls to achieve this since CoreCLR ignores a zero-byte write.
            var overlapped = new NativeOverlapped();

#if NET45
            var handle = _event.SafeWaitHandle;
#else
            var handle = _event.GetSafeWaitHandle();
#endif

            // Set the low bit to tell Windows not to send the result of this IO to the
            // completion port.
            overlapped.EventHandle = (IntPtr)(handle.DangerousGetHandle().ToInt64() | 1);
            if (WriteFile(_stream.SafePipeHandle, IntPtr.Zero, 0, IntPtr.Zero, ref overlapped) == 0)
            {
                const int ERROR_IO_PENDING = 997;
                if (Marshal.GetLastWin32Error() == ERROR_IO_PENDING)
                {
                    int written;
                    if (GetOverlappedResult(_stream.SafePipeHandle, ref overlapped, out written, 1) == 0)
                    {
                        Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                    }
                }
                else
                {
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                }
            }
        }

        public bool Peek(byte[] buffer, uint toPeek, out uint peeked, out uint available, out uint remaining)
        {
            peeked = 0;
            available = 0;
            remaining = 0;

            bool aPeekedSuccess = PeekNamedPipe(
                _stream.SafePipeHandle,
                buffer, toPeek,
                ref peeked, ref available, ref remaining);

            var error = Marshal.GetLastWin32Error();

            if (error == 0 && aPeekedSuccess)
            {
                return true;
            }

            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream.Dispose();
                _event.Dispose();
            }
        }
    }
}