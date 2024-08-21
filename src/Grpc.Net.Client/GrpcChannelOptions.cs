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

using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Compression;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client;

/// <summary>
/// An options class for configuring a <see cref="GrpcChannel"/>.
/// </summary>
public sealed class GrpcChannelOptions
{
#if SUPPORT_LOAD_BALANCING
    private TimeSpan _initialReconnectBackoff;
    private TimeSpan? _maxReconnectBackoff;
#endif

    /// <summary>
    /// Gets or sets the credentials for the channel. This setting is used to set <see cref="ChannelCredentials"/> for
    /// a channel. Connection transport layer security (TLS) is determined by the address used to create the channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The channel credentials you use must match the address TLS setting. Use <see cref="ChannelCredentials.Insecure"/>
    /// for an "http" address and <see cref="ChannelCredentials.SecureSsl"/> for "https".
    /// </para>
    /// <para>
    /// The underlying <see cref="System.Net.Http.HttpClient"/> used by the channel automatically loads root certificates
    /// from the operating system certificate store.
    /// Client certificates should be configured on HttpClient. See <see href="https://aka.ms/aspnet/grpc/certauth"/> for details.
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
    /// or hedging calls. If the buffer limit is exceeded, then no more retry attempts are made and all
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
    /// or hedging calls. If the buffer limit is exceeded, then no more retry attempts are made and all
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
    /// Gets or sets the logger factory used by the channel. If no value is specified then the channel
    /// attempts to resolve an <see cref="ILoggerFactory"/> from the <see cref="ServiceProvider"/>.
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
    /// Gets or sets a value indicating whether a gRPC call's <see cref="CallCredentials"/> are used by an insecure channel.
    /// The default value is <c>false</c>.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default value for this property is <c>false</c>, which causes an insecure channel to ignore a gRPC call's <see cref="CallCredentials"/>.
    /// Sending authentication headers over an insecure connection has security implications and shouldn't be done in production environments.
    /// </para>
    /// <para>
    /// If this property is set to <c>true</c>, call credentials are always used by a channel.
    /// </para>
    /// </remarks>
    public bool UnsafeUseInsecureChannelCallCredentials { get; set; }

    /// <summary>
    /// Gets or sets the service config for a gRPC channel. A service config allows service owners to publish parameters
    /// to be automatically used by all clients of their service. A service config can also be specified by a client
    /// using this property.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public ServiceConfig? ServiceConfig { get; set; }

#if SUPPORT_LOAD_BALANCING
    /// <summary>
    /// Gets or sets a value indicating whether resolving a service config from the <see cref="Balancer.Resolver"/>
    /// is disabled.
    /// The default value is <c>false</c>.
    /// <para>
    /// A hint is provided to the resolver that it shouldn't fetch a service config.
    /// If a service config is returned by then resolver then it is ignored.
    /// </para>
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public bool DisableResolverServiceConfig { get; set; }

    /// <summary>
    /// Gets or sets the the maximum time between subsequent connection attempts.
    /// <para>
    /// The reconnect backoff starts at an initial backoff and then exponentially increases between attempts, up to the maximum reconnect backoff.
    /// Reconnect backoff adds a jitter to randomize the backoff. This is done to avoid spikes of connection attempts.
    /// </para>
    /// <para>
    /// A <c>null</c> value removes the maximum reconnect backoff limit. The default value is 120 seconds.
    /// </para>
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public TimeSpan? MaxReconnectBackoff
    { 
        get => _maxReconnectBackoff;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentException("Maximum reconnect backoff must be greater than zero.");
            }
            _maxReconnectBackoff = value;
        }
    }

    /// <summary>
    /// Gets or sets the time between the first and second connection attempts.
    /// <para>
    /// The reconnect backoff starts at an initial backoff and then exponentially increases between attempts, up to the maximum reconnect backoff.
    /// Reconnect backoff adds a jitter to randomize the backoff. This is done to avoid spikes of connection attempts.
    /// </para>
    /// <para>
    /// Defaults to 1 second.
    /// </para>
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public TimeSpan InitialReconnectBackoff
    {
        get => _initialReconnectBackoff;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentException("Initial reconnect backoff must be greater than zero.");
            }
            _initialReconnectBackoff = value;
        }
    }
#endif

    /// <summary>
    /// Gets or sets the <see cref="IServiceProvider"/> the channel uses to resolve types.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public IServiceProvider? ServiceProvider { get; set; }

    /// <summary>
    /// Gets or sets the HTTP version to use when making gRPC calls.
    /// <para>
    /// When a <see cref="Version"/> is specified the value will be set on <see cref="HttpRequestMessage.Version"/>
    /// as gRPC calls are made. Changing this property allows the HTTP version of gRPC calls to
    /// be overridden.
    /// </para>
    /// <para>
    /// A <c>null</c> value doesn't override the HTTP version of gRPC calls. Defaults to <c>2.0</c>.
    /// </para>
    /// </summary>
    public Version? HttpVersion { get; set; }

#if NET5_0_OR_GREATER
    /// <summary>
    /// Gets or sets the HTTP policy to use when making gRPC calls.
    /// <para>
    /// When a <see cref="HttpVersionPolicy"/> is specified the value will be set on <see cref="HttpRequestMessage.VersionPolicy"/>
    /// as gRPC calls are made. The policy determines how <see cref="Version"/> is interpreted when
    /// the final HTTP version is negotiated with the server. Changing this property allows the HTTP
    /// version of gRPC calls to be overridden.
    /// </para>
    /// <para>
    /// A <c>null</c> value doesn't override the HTTP policy of gRPC calls. Defaults to <see cref="HttpVersionPolicy.RequestVersionExact"/>.
    /// </para>
    /// </summary>
    public HttpVersionPolicy? HttpVersionPolicy { get; set; }
#endif

    internal T ResolveService<T>(T defaultValue)
    {
        return (T?)ServiceProvider?.GetService(typeof(T)) ?? defaultValue;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcChannelOptions"/> class.
    /// </summary>
    public GrpcChannelOptions()
    {
        MaxReceiveMessageSize = GrpcChannel.DefaultMaxReceiveMessageSize;
#if SUPPORT_LOAD_BALANCING
        _maxReconnectBackoff = TimeSpan.FromTicks(GrpcChannel.DefaultMaxReconnectBackoffTicks);
        _initialReconnectBackoff = TimeSpan.FromTicks(GrpcChannel.DefaultInitialReconnectBackoffTicks);
#endif
        MaxRetryAttempts = GrpcChannel.DefaultMaxRetryAttempts;
        MaxRetryBufferSize = GrpcChannel.DefaultMaxRetryBufferSize;
        MaxRetryBufferPerCallSize = GrpcChannel.DefaultMaxRetryBufferPerCallSize;
    }
}
