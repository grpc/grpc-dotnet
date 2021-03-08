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

using System;
using System.Collections.Generic;
using Grpc.Core;

namespace Grpc.Net.Client.Internal
{
    internal class DefaultChannelCredentialsConfigurator : ChannelCredentialsConfiguratorBase
    {
        public bool? IsSecure { get; private set; }
        public List<CallCredentials>? CallCredentials { get; private set; }

        public override void SetCompositeCredentials(object state, ChannelCredentials channelCredentials, CallCredentials callCredentials)
        {
            channelCredentials.InternalPopulateConfiguration(this, null);

            if (callCredentials != null)
            {
                if (CallCredentials == null)
                {
                    CallCredentials = new List<CallCredentials>();
                }

                CallCredentials.Add(callCredentials);
            }
        }

        public override void SetInsecureCredentials(object state) => IsSecure = false;

        public override void SetSslCredentials(object state, string rootCertificates, KeyCertificatePair keyCertificatePair, VerifyPeerCallback verifyPeerCallback)
        {
            if (!string.IsNullOrEmpty(rootCertificates) ||
                keyCertificatePair != null ||
                verifyPeerCallback != null)
            {
                throw new InvalidOperationException(
                    $"{nameof(SslCredentials)} with non-null arguments is not supported by {nameof(GrpcChannel)}. " +
                    $"{nameof(GrpcChannel)} uses HttpClient to make gRPC calls and HttpClient automatically loads root certificates from the operating system certificate store. " +
                    $"Client certificates should be configured on HttpClient. See https://aka.ms/AA6we64 for details.");
            }

            IsSecure = true;
        }
    }
}
