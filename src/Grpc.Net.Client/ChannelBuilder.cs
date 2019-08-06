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
using System.Net.Http;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grpc.Net.Client
{
    /// <summary>
    /// A builder for configuring <see cref="GrpcChannel"/> instances.
    /// </summary>
    public sealed class ChannelBuilder
    {
        private GrpcChannelOptions _options;
        private ILoggerFactory? _loggerFactory;

        private ChannelBuilder()
        {
            _options = new GrpcChannelOptions();
        }

        /// <summary>
        /// Creates a builder that will configure the <see cref="GrpcChannel" /> to use HTTP-based transports to call to the specified URL.
        /// </summary>
        /// <param name="url">The URL the <see cref="GrpcChannel"/> will use.</param>
        /// <returns>A new instance of <see cref="ChannelBuilder"/>.</returns>
        public static ChannelBuilder ForUrl(Uri url)
        {
            var httpClient = new HttpClient() { BaseAddress = url };

            return ForHttpClient(httpClient);
        }

        /// <summary>
        /// Creates a builder that will configure the <see cref="GrpcChannel" /> to use HTTP-based transports to call to the specified URL.
        /// </summary>
        /// <param name="url">The URL the <see cref="GrpcChannel"/> will use.</param>
        /// <returns>A new instance of <see cref="ChannelBuilder"/>.</returns>
        public static ChannelBuilder ForUrl(string url)
        {
            var httpClient = new HttpClient() { BaseAddress = new Uri(url) };

            return ForHttpClient(httpClient);
        }

        /// <summary>
        /// Creates a builder that will configure the <see cref="GrpcChannel" /> to use HTTP-based transports to call with the specified <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="httpClient">The <see cref="HttpClient"/> the <see cref="GrpcChannel"/> will use.</param>
        /// <returns>A new instance of <see cref="ChannelBuilder"/>.</returns>
        public static ChannelBuilder ForHttpClient(HttpClient httpClient)
        {
            ChannelBuilder channelBuilder = new ChannelBuilder();
            channelBuilder.Configure(o =>
            {
                o.TransportOptions = new HttpClientTransportOptions
                {
                    HttpClient = httpClient
                };
            });
            return channelBuilder;
        }

        /// <summary>
        /// Configure the builder using the specified delegate. This may be called multiple times.
        /// </summary>
        /// <param name="configure">The delegate that configures the builder.</param>
        /// <returns>The same instance of the <see cref="ChannelBuilder"/> for chaining.</returns>
        public ChannelBuilder Configure(Action<GrpcChannelOptions> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            configure(_options);
            return this;
        }

        /// <summary>
        /// Sets the maximum message size in bytes that can be sent from the client.
        /// </summary>
        /// <param name="sendMaxMessageSize">The maximum message size in bytes.</param>
        /// <returns>The same instance of the <see cref="ChannelBuilder"/> for chaining.</returns>
        public ChannelBuilder SetSendMaxMessageSize(int? sendMaxMessageSize)
        {
            _options.SendMaxMessageSize = sendMaxMessageSize;
            return this;
        }

        /// <summary>
        /// Sets the maximum message size in bytes that can be received by the client.
        /// </summary>
        /// <param name="receiveMaxMessageSize">The maximum message size in bytes.</param>
        /// <returns>The same instance of the <see cref="ChannelBuilder"/> for chaining.</returns>
        public ChannelBuilder SetReceiveMaxMessageSize(int? receiveMaxMessageSize)
        {
            _options.ReceiveMaxMessageSize = receiveMaxMessageSize;
            return this;
        }

        /// <summary>
        /// Sets the credentials for the channel.
        /// </summary>
        /// <param name="credentials">The channel credentials.</param>
        /// <returns>The same instance of the <see cref="ChannelBuilder"/> for chaining.</returns>
        public ChannelBuilder SetCredentials(ChannelCredentials? credentials)
        {
            _options.Credentials = credentials;
            return this;
        }

        /// <summary>
        /// Sets the <see cref="ILoggerFactory"/> for the channel. Clients will log using the
        /// logger factory when making gRPC calls.
        /// </summary>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <returns>The same instance of the <see cref="ChannelBuilder"/> for chaining.</returns>
        public ChannelBuilder SetLoggerFactory(ILoggerFactory? loggerFactory)
        {
            _loggerFactory = loggerFactory;
            return this;
        }

        /// <summary>
        /// Creates a <see cref="GrpcChannel"/>.
        /// </summary>
        /// <returns>A <see cref="GrpcChannel"/> built using the configured options.</returns>
        public GrpcChannel Build()
        {
            if (_options.TransportOptions == null)
            {
                throw new InvalidOperationException("Unable to create channel. No transport options have been set.");
            }

            return new GrpcChannel(_options, _options.TransportOptions, _loggerFactory ?? NullLoggerFactory.Instance);
        }
    }
}
