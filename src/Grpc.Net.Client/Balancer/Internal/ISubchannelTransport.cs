﻿#region Copyright notice and license

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

#if HAVE_LOAD_BALANCING
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.Net.Client.Balancer.Internal
{
    internal interface ISubchannelTransport : IDisposable
    {
        void OnRequestComplete(CompleteContext context);
        DnsEndPoint? CurrentEndPoint { get; }

#if NET5_0_OR_GREATER
        ValueTask<Stream> GetStreamAsync(DnsEndPoint endPoint, CancellationToken cancellationToken);
#endif

#if !NETSTANDARD2_0
        ValueTask<bool>
#else
        Task<bool>
#endif
            TryConnectAsync(CancellationToken cancellationToken);

        void Disconnect();
    }

    internal interface ISubchannelTransportFactory
    {
        ISubchannelTransport Create(Subchannel subchannel);
    }
}
#endif