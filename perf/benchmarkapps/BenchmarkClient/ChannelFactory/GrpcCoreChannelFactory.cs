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

using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;

namespace BenchmarkClient.ChannelFactory
{
    public class GrpcCoreChannelFactory : IChannelFactory
    {
        private readonly string _target;
        private readonly Dictionary<int, Channel> _channels;

        public GrpcCoreChannelFactory(string target)
        {
            _target = target;
            _channels = new Dictionary<int, Channel>();
        }

        public async Task<ChannelBase> CreateAsync(int id)
        {
            if (_channels.TryGetValue(id, out var channel))
            {
                return channel;
            }

            channel = new Channel(_target, ChannelCredentials.Insecure);
            await channel.ConnectAsync();

            _channels[id] = channel;

            return channel;
        }

        public Task DisposeAsync(ChannelBase channel)
        {
            return ((Channel)channel).ShutdownAsync();
        }
    }
}
