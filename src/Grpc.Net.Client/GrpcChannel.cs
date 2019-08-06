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
using System.Diagnostics;
using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client
{
    /// <summary>
    /// The gRPC channel. A channel is used by gRPC client's to call the server.
    /// </summary>
    public sealed class GrpcChannel : ChannelBase
    {
        internal HttpClient HttpClient { get; }
        internal int? SendMaxMessageSize { get; }
        internal int? ReceiveMaxMessageSize { get; }
        internal ILoggerFactory LoggerFactory { get; }
        internal bool? IsSecure { get; }
        internal List<CallCredentials>? CallCredentials { get; }

        // Timing related options that are set in unit tests
        internal ISystemClock Clock = SystemClock.Instance;
        internal bool DisableClientDeadlineTimer { get; set; }

        internal GrpcChannel(GrpcChannelOptions channelOptions, GrpcTransportOptions transportOptions, ILoggerFactory loggerFactory) : base(transportOptions.Target)
        {
            var httpClient = (transportOptions as HttpClientTransportOptions)?.HttpClient;
            Debug.Assert(httpClient != null, "HttpClientTransportOptions should have been provided. It is the only implementation GrpcTransportOptions.");

            HttpClient = httpClient;
            SendMaxMessageSize = channelOptions.SendMaxMessageSize;
            ReceiveMaxMessageSize = channelOptions.ReceiveMaxMessageSize;
            LoggerFactory = loggerFactory;

            if (channelOptions.Credentials != null)
            {
                var configurator = new DefaultChannelCredentialsConfigurator();
                channelOptions.Credentials.InternalPopulateConfiguration(configurator, null);

                IsSecure = configurator.IsSecure;
                CallCredentials = configurator.CallCredentials;

                ValidateChannelCredentials();
            }
        }

        private void ValidateChannelCredentials()
        {
            if (IsSecure != null)
            {
                if (IsSecure.Value && HttpClient.BaseAddress.Scheme == Uri.UriSchemeHttp)
                {
                    throw new InvalidOperationException($"Channel is configured with secure channel credentials and can't use a HttpClient with a '{HttpClient.BaseAddress.Scheme}' scheme.");
                }
                if (!IsSecure.Value && HttpClient.BaseAddress.Scheme == Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException($"Channel is configured with insecure channel credentials and can't use a HttpClient with a '{HttpClient.BaseAddress.Scheme}' scheme.");
                }
            }
        }

        /// <summary>
        /// Create a new <see cref="CallInvoker"/> for the channel.
        /// </summary>
        /// <returns>A new <see cref="CallInvoker"/>.</returns>
        public override CallInvoker CreateCallInvoker()
        {
            var invoker = new HttpClientCallInvoker(this);

            return invoker;
        }

        private class DefaultChannelCredentialsConfigurator : ChannelCredentialsConfiguratorBase
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

            public override void SetInsecureCredentials(object state)
            {
                IsSecure = false;
            }

            public override void SetSslCredentials(object state, string rootCertificates, KeyCertificatePair keyCertificatePair, VerifyPeerCallback verifyPeerCallback)
            {
                if (!string.IsNullOrEmpty(rootCertificates))
                {
                    throw new InvalidOperationException($"Using an explicitly specified SSL certificate is not supported by {nameof(GrpcChannel)}.");
                }

                IsSecure = true;
            }
        }
    }
}
