using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using GRPCServer.Dotnet;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GRPCServer.Internal
{
    internal interface IServerCallHandler
    {
        Task HandleCallAsync(HttpContext httpContext);
    }

    internal class UnaryServerCallHandler<TRequest, TResponse, TImplementation> : IServerCallHandler
        where TRequest : IMessage
        where TResponse : IMessage
        where TImplementation : class
    {
        private string _methodName;
        private MessageParser _inputParser;

        public UnaryServerCallHandler(MessageParser inputParser, string methodName)
        {
            _methodName = methodName;
            _inputParser = inputParser;
        }

        public async Task HandleCallAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "application/grpc";
            httpContext.Response.Headers.Append("grpc-encoding", "identity");

            var requestPayload = await StreamUtils.ReadMessageAsync(httpContext.Request.Body);
            // TODO: make sure the payload is not null
            var request = (TRequest)_inputParser.ParseFrom(requestPayload);

            // TODO: make sure there are no more request messages.

            // Activate the implementation type via DI.
            var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TImplementation>>();
            var service = activator.Create();

            // Select procedure using reflection
            var handlerMethod = typeof(TImplementation).GetMethod(_methodName);

            // Invoke procedure
            var response = await (Task<TResponse>)handlerMethod.Invoke(service, new object[] { request, null });

            // TODO: make sure the response is not null
            var responsePayload = response.ToByteArray();

            await StreamUtils.WriteMessageAsync(httpContext.Response.Body, responsePayload, 0, responsePayload.Length);

            httpContext.Response.AppendTrailer("grpc-status", ((int)StatusCode.OK).ToString());
        }
    }

    internal class ServerStreamingServerCallHandler<TRequest, TResponse, TImplementation> : IServerCallHandler
        where TRequest : IMessage
        where TResponse : IMessage
        where TImplementation : class
    {
        private string _methodName;
        private MessageParser _inputParser;

        public ServerStreamingServerCallHandler(MessageParser inputParser, string methodName)
        {
            _methodName = methodName;
            _inputParser = inputParser;
        }

        public async Task HandleCallAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "application/grpc";
            httpContext.Response.Headers.Append("grpc-encoding", "identity");

            var requestPayload = await StreamUtils.ReadMessageAsync(httpContext.Request.Body);
            // TODO: make sure the payload is not null
            var request = (TRequest)_inputParser.ParseFrom(requestPayload);

            // TODO: make sure there are no more request messages.

            // Activate the implementation type via DI.
            var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TImplementation>>();
            var service = activator.Create();

            // Select procedure using reflection
            var handlerMethod = typeof(TImplementation).GetMethod(_methodName);

            // Invoke procedure
            await (Task)handlerMethod.Invoke(
                service,
                new object[]
                {
                    request,
                    new HttpContextStreamWriter<TResponse>(httpContext, response => response.ToByteArray()),
                    null
                });

            httpContext.Response.AppendTrailer("grpc-status", ((int)StatusCode.OK).ToString());
        }
    }

    internal class ClientStreamingServerCallHandler<TRequest, TResponse, TImplementation> : IServerCallHandler
        where TRequest : IMessage
        where TResponse : IMessage
        where TImplementation : class
    {
        private MessageParser _inputParser;
        private string _methodName;

        public ClientStreamingServerCallHandler(MessageParser inputParser, string methodName)
        {
            _methodName = methodName;
            _inputParser = inputParser;
        }

        public async Task HandleCallAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "application/grpc";
            httpContext.Response.Headers.Append("grpc-encoding", "identity");

            // Activate the implementation type via DI.
            var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TImplementation>>();
            var service = activator.Create();

            // Select procedure using reflection
            var handlerMethod = typeof(TImplementation).GetMethod(_methodName);

            // Invoke procedure
            var response = await (Task<TResponse>)handlerMethod.Invoke(
                service,
                new object[]
                {
                    new HttpContextStreamReader<TRequest>(httpContext, bytes => (TRequest)_inputParser.ParseFrom(bytes)),
                    null
                });

            // TODO: make sure the response is not null
            var responsePayload = response.ToByteArray();

            await StreamUtils.WriteMessageAsync(httpContext.Response.Body, responsePayload, 0, responsePayload.Length);

            httpContext.Response.AppendTrailer("grpc-status", ((int)StatusCode.OK).ToString());
        }
    }

    internal class DuplexStreamingServerCallHandler<TRequest, TResponse, TImplementation> : IServerCallHandler
        where TRequest : IMessage
        where TResponse : IMessage
        where TImplementation : class
    {
        private MessageParser _inputParser;
        private string _methodName;

        public DuplexStreamingServerCallHandler(MessageParser inputParser, string methodName)
        {
            _methodName = methodName;
            _inputParser = inputParser;
        }

        public async Task HandleCallAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "application/grpc";
            httpContext.Response.Headers.Append("grpc-encoding", "identity");

            // Activate the implementation type via DI.
            var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TImplementation>>();
            var service = activator.Create();

            // Select procedure using reflection
            var handlerMethod = typeof(TImplementation).GetMethod(_methodName);

            // Invoke procedure
            await (Task)handlerMethod.Invoke(
                service,
                new object[] {
                    new HttpContextStreamReader<TRequest>(httpContext, bytes => (TRequest)_inputParser.ParseFrom(bytes)),
                    new HttpContextStreamWriter<TResponse>(httpContext, response => response.ToByteArray()),
                    null
                });

            httpContext.Response.AppendTrailer("grpc-status", ((int)StatusCode.OK).ToString());
        }
    }

    internal class HttpContextStreamReader<TRequest> : IAsyncStreamReader<TRequest>
    {
        private HttpContext _httpContext;
        private Func<byte[], TRequest> _deserializer;

        public HttpContextStreamReader(HttpContext context, Func<byte[], TRequest> deserializer)
        {
            _httpContext = context;
            _deserializer = deserializer;
        }

        public TRequest Current { get; private set; }


        public ValueTask DisposeAsync()
        {
            return new ValueTask(Task.CompletedTask);
        }

        public async ValueTask<bool> MoveNextAsync()
        {
            var requestPayload = await StreamUtils.ReadMessageAsync(_httpContext.Request.Body);

            if (requestPayload == null)
            {
                Current = default(TRequest);
                return false;
            }

            Current = _deserializer(requestPayload);
            return true;
        }
    }

    internal class HttpContextStreamWriter<TResponse> : IServerStreamWriter<TResponse>
    {
        HttpContext _httpContext;
        Func<TResponse, byte[]> _serializer;

        public HttpContextStreamWriter(HttpContext context, Func<TResponse, byte[]> serializer)
        {
            _httpContext = context;
            _serializer = serializer;
        }

        public WriteOptions WriteOptions { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public Task WriteAsync(TResponse message)
        {
            // TODO: make sure the response is not null
            var responsePayload = _serializer(message);
            return StreamUtils.WriteMessageAsync(_httpContext.Response.Body, responsePayload, 0, responsePayload.Length);
        }
    }

    // utilities for parsing gRPC messages from request/response streams
    // NOTE: implementations are not efficient and are very GC heavy
    internal class StreamUtils
    {
        const int MessageDelimiterSize = 4;  // how many bytes it takes to encode "Message-Length"

        // reads a grpc message
        // returns null if there are no more messages.
        public static async Task<byte[]> ReadMessageAsync(Stream stream)
        {
            // read Compressed-Flag and Message-Length
            // as described in https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-HTTP2.md
            var delimiterBuffer = new byte[1 + MessageDelimiterSize];
            if (!await ReadExactlyBytesOrNothing(stream, delimiterBuffer, 0, delimiterBuffer.Length))
            {
                return null;
            }

            var compressionFlag = delimiterBuffer[0];
            var messageLength = DecodeMessageLength(new ReadOnlySpan<byte>(delimiterBuffer, 1, MessageDelimiterSize));

            if (compressionFlag != 0)
            {
                // TODO(jtattermusch): support compressed messages
                throw new IOException("Compressed messages are not yet supported.");
            }

            var msgBuffer = new byte[messageLength];
            if (!await ReadExactlyBytesOrNothing(stream, msgBuffer, 0, msgBuffer.Length))
            {
                throw new IOException("Unexpected end of stream.");
            }
            return msgBuffer;
        }

        public static async Task WriteMessageAsync(Stream stream, byte[] buffer, int offset, int count)
        {
            var delimiterBuffer = new byte[1 + MessageDelimiterSize];
            delimiterBuffer[0] = 0; // = non-compressed
            EncodeMessageLength(count, new Span<byte>(delimiterBuffer, 1, MessageDelimiterSize));
            await stream.WriteAsync(delimiterBuffer, 0, delimiterBuffer.Length);

            await stream.WriteAsync(buffer, offset, count);
        }

        // reads exactly the number of requested bytes (returns true if successfully read).
        // returns false if we reach end of stream before successfully reading anything.
        // throws if stream ends in the middle of reading.
        private static async Task<bool> ReadExactlyBytesOrNothing(Stream stream, byte[] buffer, int offset, int count)
        {
            bool noBytesRead = true;
            while (count > 0)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset, count);
                if (bytesRead == 0)
                {
                    if (noBytesRead)
                    {
                        return false;
                    }
                    throw new IOException("Unexpected end of stream.");
                }
                noBytesRead = false;
                offset += bytesRead;
                count -= bytesRead;
            }
            return true;
        }

        private static void EncodeMessageLength(int messageLength, Span<byte> dest)
        {
            if (dest.Length < MessageDelimiterSize)
            {
                throw new ArgumentException("Buffer too small to decode message length.");
            }

            ulong unsignedValue = (ulong)messageLength;
            for (int i = MessageDelimiterSize - 1; i >= 0; i--)
            {
                // msg length stored in big endian
                dest[i] = (byte)(unsignedValue & 0xff);
                unsignedValue >>= 8;
            }
        }

        private static int DecodeMessageLength(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < MessageDelimiterSize)
            {
                throw new ArgumentException("Buffer too small to decode message length.");
            }

            ulong result = 0;
            for (int i = 0; i < MessageDelimiterSize; i++)
            {
                // msg length stored in big endian
                result = (result << 8) + buffer[i];
            }

            if (result > int.MaxValue)
            {
                throw new IOException("Message too large: " + result);
            }
            return (int)result;
        }

    }
}
