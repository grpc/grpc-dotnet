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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
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
    public sealed class GrpcChannel : ChannelBase, IDisposable
    {
        internal const int DefaultMaxReceiveMessageSize = 1024 * 1024 * 4; // 4 MB

        private readonly ConcurrentDictionary<IMethod, GrpcMethodInfo> _methodInfoCache;
        private readonly Func<IMethod, GrpcMethodInfo> _createMethodInfoFunc;

        internal Uri Address { get; }
        internal HttpClient HttpClient { get; }
        internal int? SendMaxMessageSize { get; }
        internal int? ReceiveMaxMessageSize { get; }
        internal ILoggerFactory LoggerFactory { get; }
        internal bool ThrowOperationCanceledOnCancellation { get; }
        internal bool? IsSecure { get; }
        internal List<CallCredentials>? CallCredentials { get; }
        internal Dictionary<string, ICompressionProvider> CompressionProviders { get; }
        internal string MessageAcceptEncoding { get; }
        internal bool Disposed { get; private set; }
        // Timing related options that are set in unit tests
        internal ISystemClock Clock = SystemClock.Instance;
        internal bool DisableClientDeadlineTimer { get; set; }

        private bool _shouldDisposeHttpClient;

        internal GrpcChannel(Uri address, GrpcChannelOptions channelOptions) : base(address.Authority)
        {
            _methodInfoCache = new ConcurrentDictionary<IMethod, GrpcMethodInfo>();

            // Dispose the HttpClient if...
            //   1. No client was specified and so the channel created the HttpClient itself
            //   2. User has specified a client and set DisposeHttpClient to true
            _shouldDisposeHttpClient = channelOptions.HttpClient == null || channelOptions.DisposeHttpClient;

            Address = address;
            HttpClient = channelOptions.HttpClient ?? new HttpClient();
            SendMaxMessageSize = channelOptions.MaxSendMessageSize;
            ReceiveMaxMessageSize = channelOptions.MaxReceiveMessageSize;
            CompressionProviders = ResolveCompressionProviders(channelOptions.CompressionProviders);
            MessageAcceptEncoding = GrpcProtocolHelpers.GetMessageAcceptEncoding(CompressionProviders);
            LoggerFactory = channelOptions.LoggerFactory ?? NullLoggerFactory.Instance;
            ThrowOperationCanceledOnCancellation = channelOptions.ThrowOperationCanceledOnCancellation;
            _createMethodInfoFunc = CreateMethodInfo;

            if (channelOptions.Credentials != null)
            {
                var configurator = new DefaultChannelCredentialsConfigurator();
                channelOptions.Credentials.InternalPopulateConfiguration(configurator, null);

                IsSecure = configurator.IsSecure;
                CallCredentials = configurator.CallCredentials;

                ValidateChannelCredentials();
            }
        }

        internal GrpcMethodInfo GetCachedGrpcMethodInfo(IMethod method)
        {
            return _methodInfoCache.GetOrAdd(method, _createMethodInfoFunc);
        }

        private GrpcMethodInfo CreateMethodInfo(IMethod method)
        {
            var uri = new Uri(method.FullName, UriKind.Relative);
            var scope = new GrpcCallScope(method.Type, uri);

            return new GrpcMethodInfo(scope, new Uri(Address, uri));
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
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(GrpcChannel));
            }

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

        /// <summary>
        /// Releases the resources used by the <see cref="GrpcChannel"/> class.
        /// Clients created with the channel can't be used after the channel is disposed.
        /// </summary>
        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            if (_shouldDisposeHttpClient)
            {
                HttpClient.Dispose();
            }
            Disposed = true;
        }
    }
}
