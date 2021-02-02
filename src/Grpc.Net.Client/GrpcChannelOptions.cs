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
using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Compression;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client
{
    /// <summary>
    /// An options class for configuring a <see cref="GrpcChannel"/>.
    /// </summary>
    public sealed class GrpcChannelOptions
    {
        /// <summary>
        /// Gets or sets the credentials for the channel. This setting is used to set <see cref="CallCredentials"/> for
        /// a channel. Connection transport layer security (TLS) is determined by the address used to create the channel.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The channel credentials you use must match the address TLS setting. Use <see cref="ChannelCredentials.Insecure"/>
        /// for an "http" address and <see cref="SslCredentials"/> with no arguments for "https".
        /// </para>
        /// <para>
        /// The underlying <see cref="System.Net.Http.HttpClient"/> used by the channel automatically loads root certificates
        /// from the operating system certificate store.
        /// Client certificates should be configured on HttpClient. See https://aka.ms/AA6we64 for details.
        /// </para>
        /// </remarks>
        public ChannelCredentials? Credentials { get; set; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be sent from the client. Attempting to send a message
        /// that exceeds the configured maximum message size results in an exception.
        /// <para>
        /// A <c>null</c> value removes the maximum message size limit. Defaults to <c>null</c>.
        /// </para>
        /// </summary>
        public int? MaxSendMessageSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be received by the client. If the client receives a
        /// message that exceeds this limit, it throws an exception.
        /// <para>
        /// A <c>null</c> value removes the maximum message size limit. Defaults to 4,194,304 (4 MB).
        /// </para>
        /// </summary>
        public int? MaxReceiveMessageSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum retry attempts. This value limits any retry and hedging attempt values specified in
        /// the service config.
        /// <para>
        /// Setting this value alone doesn't enable retries. Retries are enabled in the service config, which can be done
        /// using <see cref="ServiceConfig"/>.
        /// </para>
        /// <para>
        /// A <c>null</c> value removes the maximum retry attempts limit. Defaults to 5.
        /// </para>
        /// <para>
        /// Note: Experimental API that can change or be removed without any prior notice.
        /// </para>
        /// </summary>
        public int? MaxRetryAttempts { get; set; }

        /// <summary>
        /// Gets or sets the maximum buffer size in bytes that can be used to store sent messages when retrying
        /// or hedging calls. If the buffer limit is exceeded then no more retry attempts are made and all
        /// hedging calls but one will be canceled. This limit is applied across all calls made using the channel.
        /// <para>
        /// Setting this value alone doesn't enable retries. Retries are enabled in the service config, which can be done
        /// using <see cref="ServiceConfig"/>.
        /// </para>
        /// <para>
        /// A <c>null</c> value removes the maximum retry buffer size limit. Defaults to 16,777,216 (16 MB).
        /// </para>
        /// <para>
        /// Note: Experimental API that can change or be removed without any prior notice.
        /// </para>
        /// </summary>
        public long? MaxRetryBufferSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum buffer size in bytes that can be used to store sent messages when retrying
        /// or hedging calls. If the buffer limit is exceeded then no more retry attempts are made and all
        /// hedging calls but one will be canceled. This limit is applied to one call.
        /// <para>
        /// Setting this value alone doesn't enable retries. Retries are enabled in the service config, which can be done
        /// using <see cref="ServiceConfig"/>.
        /// </para>
        /// <para>
        /// A <c>null</c> value removes the maximum retry buffer size limit per call. Defaults to 1,048,576 (1 MB).
        /// </para>
        /// <para>
        /// Note: Experimental API that can change or be removed without any prior notice.
        /// </para>
        /// </summary>
        public long? MaxRetryBufferPerCallSize { get; set; }

        /// <summary>
        /// Gets or sets a collection of compression providers.
        /// </summary>
        public IList<ICompressionProvider>? CompressionProviders { get; set; }

        /// <summary>
        /// Gets or sets the logger factory used by the channel.
        /// </summary>
        public ILoggerFactory? LoggerFactory { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="System.Net.Http.HttpClient"/> used by the channel to make HTTP calls.
        /// </summary>
        /// <remarks>
        /// <para>
        /// By default a <see cref="System.Net.Http.HttpClient"/> specified here will not be disposed with the channel.
        /// To dispose the <see cref="System.Net.Http.HttpClient"/> with the channel you must set <see cref="DisposeHttpClient"/>
        /// to <c>true</c>.
        /// </para>
        /// <para>
        /// Only one HTTP caller can be specified for a channel. An error will be thrown if this is configured
        /// together with <see cref="HttpHandler"/>.
        /// </para>
        /// </remarks>
        public HttpClient? HttpClient { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="HttpMessageHandler"/> used by the channel to make HTTP calls.
        /// </summary>
        /// <remarks>
        /// <para>
        /// By default a <see cref="HttpMessageHandler"/> specified here will not be disposed with the channel.
        /// To dispose the <see cref="HttpMessageHandler"/> with the channel you must set <see cref="DisposeHttpClient"/>
        /// to <c>true</c>.
        /// </para>
        /// <para>
        /// Only one HTTP caller can be specified for a channel. An error will be thrown if this is configured
        /// together with <see cref="HttpClient"/>.
        /// </para>
        /// </remarks>
        public HttpMessageHandler? HttpHandler { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the underlying <see cref="System.Net.Http.HttpClient"/> or 
        /// <see cref="HttpMessageHandler"/> should be disposed when the <see cref="GrpcChannel"/> instance is disposed.
        /// The default value is <c>false</c>.
        /// </summary>
        /// <remarks>
        /// This setting is used when a <see cref="HttpClient"/> or <see cref="HttpHandler"/> value is specified.
        /// If they are not specified then the channel will create an internal HTTP caller that is always disposed
        /// when the channel is disposed.
        /// </remarks>
        public bool DisposeHttpClient { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether clients will throw <see cref="OperationCanceledException"/> for a call when its
        /// <see cref="CallOptions.CancellationToken"/> is triggered or its <see cref="CallOptions.Deadline"/> is exceeded.
        /// The default value is <c>false</c>.
        /// <para>
        /// Note: Experimental API that can change or be removed without any prior notice.
        /// </para>
        /// </summary>
        public bool ThrowOperationCanceledOnCancellation { get; set; }

        /// <summary>
        /// Gets or sets the service config for a gRPC channel. A service config allows service owners to publish parameters
        /// to be automatically used by all clients of their service. A service config can also be specified by a client
        /// using this property.
        /// <para>
        /// Note: Experimental API that can change or be removed without any prior notice.
        /// </para>
        /// </summary>
        public ServiceConfig? ServiceConfig { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcChannelOptions"/> class.
        /// </summary>
        public GrpcChannelOptions()
        {
            MaxReceiveMessageSize = GrpcChannel.DefaultMaxReceiveMessageSize;
            MaxRetryAttempts = GrpcChannel.DefaultMaxRetryAttempts;
            MaxRetryBufferSize = GrpcChannel.DefaultMaxRetryBufferSize;
            MaxRetryBufferPerCallSize = GrpcChannel.DefaultMaxRetryBufferPerCallSize;
        }
    }
}
