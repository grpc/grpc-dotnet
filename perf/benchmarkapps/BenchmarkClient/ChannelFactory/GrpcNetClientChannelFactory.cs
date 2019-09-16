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

using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;

namespace BenchmarkClient.ChannelFactory
{
    public class GrpcNetClientChannelFactory : IChannelFactory
    {
        private readonly string _target;
        private readonly bool _useClientCertificate;

        public GrpcNetClientChannelFactory(string target, bool useClientCertificate)
        {
            _target = target;
            _useClientCertificate = useClientCertificate;
        }

        public Task<ChannelBase> CreateAsync()
        {
            var url = _useClientCertificate ? "https://" : "http://";
            url += _target;

            var channel = GrpcChannel.ForAddress(url);

            return Task.FromResult<ChannelBase>(channel);
        }

        public Task DisposeAsync(ChannelBase channel)
        {
            ((GrpcChannel)channel).Dispose();
            return Task.CompletedTask;
        }
    }
}
