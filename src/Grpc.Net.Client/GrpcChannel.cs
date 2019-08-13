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
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Threading;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grpc.Net.Client
{
    /// <summary>
    /// Represents a gRPC channel. Channels are an abstraction of long-lived connections to remote servers.
    /// Client objects can reuse the same channel. Creating a channel is an expensive operation compared to invoking
    /// a remote call so in general you should reuse a single channel for as many calls as possible.
    /// </summary>
    public sealed class GrpcChannel : ChannelBase
    {
        internal const int DefaultReceiveMaxMessageSize = 1024 * 1024 * 4; // 4 MB

        internal Uri Address { get; }
        internal HttpClient HttpClient { get; }
        internal int? SendMaxMessageSize { get; }
        internal int? ReceiveMaxMessageSize { get; }
        internal ILoggerFactory LoggerFactory { get; }
        internal bool? IsSecure { get; }
        internal List<CallCredentials>? CallCredentials { get; }
        internal Dictionary<string, ICompressionProvider> CompressionProviders { get; }
        internal string MessageAcceptEncoding { get; }

        // Timing related options that are set in unit tests
        internal ISystemClock Clock = SystemClock.Instance;
        internal bool DisableClientDeadlineTimer { get; set; }

        internal GrpcChannel(Uri address, GrpcChannelOptions channelOptions) : base(address.Authority)
        {
            Address = address;
            HttpClient = channelOptions.HttpClient ?? new HttpClient();
            SendMaxMessageSize = channelOptions.SendMaxMessageSize;
            ReceiveMaxMessageSize = channelOptions.ReceiveMaxMessageSize;
            CompressionProviders = ResolveCompressionProviders(channelOptions.CompressionProviders);
            MessageAcceptEncoding = GrpcProtocolHelpers.GetMessageAcceptEncoding(CompressionProviders);
            LoggerFactory = channelOptions.LoggerFactory ?? NullLoggerFactory.Instance;

            if (channelOptions.Credentials != null)
            {
                var configurator = new DefaultChannelCredentialsConfigurator();
                channelOptions.Credentials.InternalPopulateConfiguration(configurator, null);

                IsSecure = configurator.IsSecure;
                CallCredentials = configurator.CallCredentials;

                ValidateChannelCredentials();
            }
        }

        private static Dictionary<string, ICompressionProvider> ResolveCompressionProviders(IList<ICompressionProvider>? compressionProviders)
        {
            if (compressionProviders == null)
            {
                return GrpcProtocolConstants.DefaultCompressionProviders;
            }

            var resolvedCompressionProviders = new Dictionary<string, ICompressionProvider>(StringComparer.Ordinal);
            for (int i = 0; i < compressionProviders.Count; i++)
            {
                var compressionProvider = compressionProviders[i];
                if (!resolvedCompressionProviders.ContainsKey(compressionProvider.EncodingName))
                {
                    resolvedCompressionProviders.Add(compressionProvider.EncodingName, compressionProvider);
                }
            }

            return resolvedCompressionProviders;
        }

        private void ValidateChannelCredentials()
        {
            if (IsSecure != null)
            {
                if (IsSecure.Value && Address.Scheme == Uri.UriSchemeHttp)
                {
                    throw new InvalidOperationException($"Channel is configured with secure channel credentials and can't use a HttpClient with a '{Address.Scheme}' scheme.");
                }
                if (!IsSecure.Value && Address.Scheme == Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException($"Channel is configured with insecure channel credentials and can't use a HttpClient with a '{Address.Scheme}' scheme.");
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
                if (!string.IsNullOrEmpty(rootCertificates) ||
                    keyCertificatePair != null ||
                    verifyPeerCallback != null)
                {
                    throw new InvalidOperationException($"Using {nameof(SslCredentials)} with non-null arguments is not supported by {nameof(GrpcChannel)}.");
                }

                IsSecure = true;
            }
        }

        /// <summary>
        /// Creates a <see cref="GrpcChannel"/> for the specified address.
        /// </summary>
        /// <param name="address">The address the channel will use.</param>
        /// <returns>A new instance of <see cref="GrpcChannel"/>.</returns>
        public static GrpcChannel ForAddress(string address)
        {
            return ForAddress(address, new GrpcChannelOptions());
        }

        /// <summary>
        /// Creates a <see cref="GrpcChannel"/> for the specified address and configuration options.
        /// </summary>
        /// <param name="address">The address the channel will use.</param>
        /// <param name="channelOptions">The channel configuration options.</param>
        /// <returns>A new instance of <see cref="GrpcChannel"/>.</returns>
        public static GrpcChannel ForAddress(string address, GrpcChannelOptions channelOptions)
        {
            return ForAddress(new Uri(address), channelOptions);
        }

        /// <summary>
        /// Creates a <see cref="GrpcChannel"/> for the specified address.
        /// </summary>
        /// <param name="address">The address the channel will use.</param>
        /// <returns>A new instance of <see cref="GrpcChannel"/>.</returns>
        public static GrpcChannel ForAddress(Uri address)
        {
            return ForAddress(address, new GrpcChannelOptions());
        }

        /// <summary>
        /// Creates a <see cref="GrpcChannel"/> for the specified address and configuration options.
        /// </summary>
        /// <param name="address">The address the channel will use.</param>
        /// <param name="channelOptions">The channel configuration options.</param>
        /// <returns>A new instance of <see cref="GrpcChannel"/>.</returns>
        public static GrpcChannel ForAddress(Uri address, GrpcChannelOptions channelOptions)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            if (channelOptions == null)
            {
                throw new ArgumentNullException(nameof(channelOptions));
            }

            return new GrpcChannel(address, channelOptions);
        }
    }
}
