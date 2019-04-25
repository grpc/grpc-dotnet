using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;

namespace InteropTestsClient
{
    public interface IChannel
    {
        Task ShutdownAsync();
    }

    public class HttpClientChannel : IChannel
    {
        public Task ShutdownAsync()
        {
            return Task.CompletedTask;
        }
    }

    public class CoreChannel : IChannel
    {
        public Channel Channel { get; }

        public CoreChannel(Channel channel)
        {
            Channel = channel;
        }

        public Task ShutdownAsync()
        {
            return Channel.ShutdownAsync();
        }
    }
}
