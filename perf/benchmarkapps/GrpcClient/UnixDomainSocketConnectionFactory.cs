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

using System.Net;
using System.Net.Connections;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GrpcClient
{
    // Removed in .NET 5. Re-enable when support is added back in .NET 6
    /*
    public class UnixDomainSocketConnectionFactory : SocketsConnectionFactory
    {
        private readonly EndPoint _endPoint;

        public UnixDomainSocketConnectionFactory(EndPoint endPoint) : base(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
        {
            _endPoint = endPoint;
        }

        public override ValueTask<Connection> ConnectAsync(EndPoint? endPoint, IConnectionProperties? options = null, CancellationToken cancellationToken = default)
        {
            return base.ConnectAsync(_endPoint, options, cancellationToken);
        }
    }
    */
}
