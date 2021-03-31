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
using System.Linq;
using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Configuration;
//using GrpcServiceConfig = Grpc.Net.Client.Configuration.ServiceConfig;
using Grpc.Net.Compression;
using Grpc.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Grpc.Net.Client.Internal.Retry;
using System.Threading;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;

namespace Grpc.Net.Client
{
    /// <summary>
    /// Represents a gRPC channel. Channels are an abstraction of long-lived connections to remote servers.
    /// Client objects can reuse the same channel. Creating a channel is an expensive operation compared to invoking
    /// a remote call so in general you should reuse a single channel for as many calls as possible.
    /// </summary>
    public sealed class GrpcChannel : ChannelBase, IDisposable, ISubchannelTransportFactory
    {
        internal const int DefaultMaxReceiveMessageSize = 1024 * 1024 * 4; // 4 MB
        internal const int DefaultMaxRetryAttempts = 5;
        internal const long DefaultMaxRetryBufferSize = 1024 * 1024 * 16; // 16 MB
        internal const long DefaultMaxRetryBufferPerCallSize = 1024 * 1024; // 1 MB

        private readonly object _lock;
        private readonly ConcurrentDictionary<IMethod, GrpcMethodInfo> _methodInfoCache;
        private readonly Func<IMethod, GrpcMethodInfo> _createMethodInfoFunc;
        private readonly Dictionary<MethodKey, MethodConfig>? _serviceConfigMethods;
        // Internal for testing
        internal readonly HashSet<IDisposable> ActiveCalls;

        internal Uri Address { get; }
        internal HttpMessageInvoker HttpInvoker { get; }
        internal HttpHandlerType HttpHandlerType { get; }
        internal int? SendMaxMessageSize { get; }
        internal int? ReceiveMaxMessageSize { get; }
        internal int? MaxRetryAttempts { get; }
        internal long? MaxRetryBufferSize { get; }
        internal long? MaxRetryBufferPerCallSize { get; }
        internal ILoggerFactory LoggerFactory { get; }
        internal ILogger Logger { get; }
        internal bool ThrowOperationCanceledOnCancellation { get; }
        internal bool IsSecure { get; }
        internal List<CallCredentials>? CallCredentials { get; }
        internal Dictionary<string, ICompressionProvider> CompressionProviders { get; }
        internal string MessageAcceptEncoding { get; }
        internal bool Disposed { get; private set; }

        // Load balancing
        internal Resolver Resolver { get; }
        internal ConnectionManager ConnectionManager { get; }

        // Stateful
        internal ChannelRetryThrottling? RetryThrottling { get; }
        internal long CurrentRetryBufferSize;

        // Options that are set in unit tests
        internal ISystemClock Clock = SystemClock.Instance;
        internal IOperatingSystem OperatingSystem = Internal.OperatingSystem.Instance;
        internal IRandomGenerator RandomGenerator;
        internal bool DisableClientDeadline;
        internal long MaxTimerDueTime = uint.MaxValue - 1; // Max System.Threading.Timer due time

        private readonly bool _shouldDisposeHttpClient;

        private T ResolveService<T>(IServiceProvider? serviceProvider, T defaultValue)
        {
            return (T?)serviceProvider?.GetService(typeof(T)) ?? defaultValue;
        }

        internal GrpcChannel(Uri address, GrpcChannelOptions channelOptions) : base(address.Authority)
        {
            _lock = new object();
            _methodInfoCache = new ConcurrentDictionary<IMethod, GrpcMethodInfo>();

            // Dispose the HTTP client/handler if...
            //   1. No client/handler was specified and so the channel created the client itself
            //   2. User has specified a client/handler and set DisposeHttpClient to true
            _shouldDisposeHttpClient = (channelOptions.HttpClient == null && channelOptions.HttpHandler == null)
                || channelOptions.DisposeHttpClient;

            Address = address;
            LoggerFactory = channelOptions.LoggerFactory ?? ResolveService<ILoggerFactory>(channelOptions.ServiceProvider, NullLoggerFactory.Instance);
            RandomGenerator = ResolveService<IRandomGenerator>(channelOptions.ServiceProvider, new RandomGenerator());

            if (Address.Scheme == Uri.UriSchemeHttps || Address.Scheme == Uri.UriSchemeHttp)
            {
                Resolver = new StaticResolver(new[] { new DnsEndPoint(Address.Host, Address.Port) });
            }
            else
            {
                Resolver = CreateResolver(channelOptions);
            }

            var transportFactory = ResolveService<ISubchannelTransportFactory>(channelOptions.ServiceProvider, this);

            ConnectionManager = new ConnectionManager(
                Resolver,
                channelOptions.DisableResolverServiceConfig,
                LoggerFactory,
                transportFactory,
                ResolveLoadBalancerFactories(channelOptions.ServiceProvider));
            ConnectionManager.ConfigureBalancer(c => new ChildHandlerLoadBalancer(
                c,
                channelOptions.ServiceConfig,
                ConnectionManager));

            HttpHandlerType = CalculateHandlerType(channelOptions);
            HttpInvoker = channelOptions.HttpClient ?? CreateInternalHttpInvoker(channelOptions.HttpHandler);
            SendMaxMessageSize = channelOptions.MaxSendMessageSize;
            ReceiveMaxMessageSize = channelOptions.MaxReceiveMessageSize;
            MaxRetryAttempts = channelOptions.MaxRetryAttempts;
            MaxRetryBufferSize = channelOptions.MaxRetryBufferSize;
            MaxRetryBufferPerCallSize = channelOptions.MaxRetryBufferPerCallSize;
            CompressionProviders = ResolveCompressionProviders(channelOptions.CompressionProviders);
            MessageAcceptEncoding = GrpcProtocolHelpers.GetMessageAcceptEncoding(CompressionProviders);
            Logger = LoggerFactory.CreateLogger<GrpcChannel>();
            ThrowOperationCanceledOnCancellation = channelOptions.ThrowOperationCanceledOnCancellation;
            _createMethodInfoFunc = CreateMethodInfo;
            ActiveCalls = new HashSet<IDisposable>();
            if (channelOptions.ServiceConfig is { } serviceConfig)
            {
                RetryThrottling = serviceConfig.RetryThrottling != null ? CreateChannelRetryThrottling(serviceConfig.RetryThrottling) : null;
                _serviceConfigMethods = CreateServiceConfigMethods(serviceConfig);
            }

            if (channelOptions.Credentials != null)
            {
                var configurator = new DefaultChannelCredentialsConfigurator();
                channelOptions.Credentials.InternalPopulateConfiguration(configurator, null);

                IsSecure = configurator.IsSecure ?? false;
                CallCredentials = configurator.CallCredentials;

                ValidateChannelCredentials();
            }
            else
            {
                if (Address.Scheme == Uri.UriSchemeHttp)
                {
                    IsSecure = false;
                }
                else if (Address.Scheme == Uri.UriSchemeHttps)
                {
                    IsSecure = true;
                }
                else
                {
                    throw new InvalidOperationException($"Unable to determine the TLS configuration of the channel from address '{Address}'. " +
                        $"{nameof(GrpcChannelOptions)}.{nameof(GrpcChannelOptions.Credentials)} must be specified when the address doesn't have a 'http' or 'https' scheme. " +
                        "To call TLS endpoints, set credentials to 'new SslCredentials()'. " +
                        "To call non-TLS endpoints, set credentials to 'ChannelCredentials.Insecure'.");
                }
            }

            // Non-HTTP addresses (e.g. dns:///custom-hostname) usually specify a path instead of a host.
            // Only log about a path being present if HTTP or HTTPS.
            if (!string.IsNullOrEmpty(Address.PathAndQuery) &&
                Address.PathAndQuery != "/" &&
                (Address.Scheme == Uri.UriSchemeHttps || Address.Scheme == Uri.UriSchemeHttp))
            {
                Log.AddressPathUnused(Logger, Address.OriginalString);
            }
        }

        private static HttpHandlerType CalculateHandlerType(GrpcChannelOptions channelOptions)
        {
            if (channelOptions.HttpHandler == null)
            {
                // No way to know what handler a HttpClient is using so be same and assume custom.
                return channelOptions.HttpClient == null
                    ? HttpHandlerType.Default
                    : HttpHandlerType.Custom;
            }

            return HttpHandlerFactory.CalculateHandlerType(channelOptions.HttpHandler);
        }

        private Resolver CreateResolver(GrpcChannelOptions options)
        {
            var factories = ResolveService<IEnumerable<ResolverFactory>>(options.ServiceProvider, Array.Empty<ResolverFactory>());
            factories = factories.Union(new[] { new DnsResolverFactory(LoggerFactory, Timeout.InfiniteTimeSpan) });

            foreach (var factory in factories)
            {
                if (string.Equals(factory.Name, Address.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    return factory.Create(Address, new ResolverOptions(options.DisableResolverServiceConfig));
                }
            }

            throw new InvalidOperationException($"No address resolver configured for the scheme '{Address.Scheme}'.");
        }

        private LoadBalancerFactory[] ResolveLoadBalancerFactories(IServiceProvider? serviceProvider)
        {
            var resolvedFactories = new LoadBalancerFactory[] { new PickFirstBalancerFactory(LoggerFactory), new RoundRobinBalancerFactory(LoggerFactory) };

            var serviceFactories = ResolveService<IEnumerable<LoadBalancerFactory>?>(serviceProvider, defaultValue: null);
            if (serviceFactories != null)
            {
                resolvedFactories = serviceFactories.Union(resolvedFactories).ToArray();
            }
            
            return resolvedFactories;
        }

        private ChannelRetryThrottling CreateChannelRetryThrottling(RetryThrottlingPolicy retryThrottling)
        {
            if (retryThrottling.MaxTokens == null)
            {
                throw CreateException(RetryThrottlingPolicy.MaxTokensPropertyName);
            }
            if (retryThrottling.TokenRatio == null)
            {
                throw CreateException(RetryThrottlingPolicy.TokenRatioPropertyName);
            }

            return new ChannelRetryThrottling(retryThrottling.MaxTokens.GetValueOrDefault(), retryThrottling.TokenRatio.GetValueOrDefault(), LoggerFactory);

            static InvalidOperationException CreateException(string propertyName)
            {
                return new InvalidOperationException($"Retry throttling missing required property '{propertyName}'.");
            }
        }

        private static Dictionary<MethodKey, MethodConfig> CreateServiceConfigMethods(ServiceConfig serviceConfig)
        {
            var configs = new Dictionary<MethodKey, MethodConfig>();
            for (var i = 0; i < serviceConfig.MethodConfigs.Count; i++)
            {
                var methodConfig = serviceConfig.MethodConfigs[i];
                for (var j = 0; j < methodConfig.Names.Count; j++)
                {
                    var name = methodConfig.Names[j];
                    var methodKey = new MethodKey(name.Service, name.Method);
                    if (configs.ContainsKey(methodKey))
                    {
                        throw new InvalidOperationException($"Duplicate method config found. Service: '{name.Service}', method: '{name.Method}'.");
                    }
                    configs[methodKey] = methodConfig;
                }
            }

            return configs;
        }

        private HttpMessageInvoker CreateInternalHttpInvoker(HttpMessageHandler? handler)
        {
            // HttpMessageInvoker should always dispose handler if Disposed is called on it.
            // Decision to dispose invoker is controlled by _shouldDisposeHttpClient.
            if (handler == null)
            {
                handler = HttpHandlerFactory.CreatePrimaryHandler();
            }

#if NET5_0
            handler = HttpHandlerFactory.EnsureTelemetryHandler(handler);
#endif

            handler = new BalancerHttpHandler(handler, ConnectionManager);

            // Use HttpMessageInvoker instead of HttpClient because it is faster
            // and we don't need client's features.
            var httpInvoker = new HttpMessageInvoker(handler, disposeHandler: true);

            return httpInvoker;
        }

        internal void RegisterActiveCall(IDisposable grpcCall)
        {
            lock (_lock)
            {
                ActiveCalls.Add(grpcCall);
            }
        }

        internal void FinishActiveCall(IDisposable grpcCall)
        {
            lock (_lock)
            {
                ActiveCalls.Remove(grpcCall);
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
            var methodConfig = ResolveMethodConfig(method);

            var uriBuilder = new UriBuilder(Address);
            uriBuilder.Scheme = IsSecure ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            uriBuilder.Path = method.FullName;

            // Triple slash URIs, e.g. dns:///custom-value, won't have a host and UriBuilder throws an error.
            // Add a temp value as the host. This will get replaced in the HTTP request URI by the load balancer.
            if (string.IsNullOrEmpty(uriBuilder.Host))
            {
                uriBuilder.Host = "tempuri.org";
            }

            return new GrpcMethodInfo(scope, uriBuilder.Uri, methodConfig);
        }

        private MethodConfig? ResolveMethodConfig(IMethod method)
        {
            if (_serviceConfigMethods != null)
            {
                MethodConfig? methodConfig;
                if (_serviceConfigMethods.TryGetValue(new MethodKey(method.ServiceName, method.Name), out methodConfig))
                {
                    return methodConfig;
                }
                if (_serviceConfigMethods.TryGetValue(new MethodKey(method.ServiceName, null), out methodConfig))
                {
                    return methodConfig;
                }
                if (_serviceConfigMethods.TryGetValue(new MethodKey(null, null), out methodConfig))
                {
                    return methodConfig;
                }
            }

            return null;
        }

        private static Dictionary<string, ICompressionProvider> ResolveCompressionProviders(IList<ICompressionProvider>? compressionProviders)
        {
            if (compressionProviders == null)
            {
                return GrpcProtocolConstants.DefaultCompressionProviders;
            }

            var resolvedCompressionProviders = new Dictionary<string, ICompressionProvider>(StringComparer.Ordinal);
            for (var i = 0; i < compressionProviders.Count; i++)
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
            if (IsSecure && Address.Scheme == Uri.UriSchemeHttp)
            {
                throw new InvalidOperationException($"Channel is configured with secure channel credentials and can't use a HttpClient with a '{Address.Scheme}' scheme.");
            }
            if (!IsSecure && Address.Scheme == Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException($"Channel is configured with insecure channel credentials and can't use a HttpClient with a '{Address.Scheme}' scheme.");
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

            if (channelOptions.HttpClient != null && channelOptions.HttpHandler != null)
            {
                throw new ArgumentException($"{nameof(GrpcChannelOptions.HttpClient)} and {nameof(GrpcChannelOptions.HttpHandler)} have been configured. " +
                    $"Only one HTTP caller can be specified.");
            }

            return new GrpcChannel(address, channelOptions);
        }

        /// <summary>
        /// Gets current connectivity state of this channel.
        /// After the channel has been shutdown, <see cref="ConnectivityState.Shutdown"/> is returned.
        /// </summary>
        public ConnectivityState State => ConnectionManager.State;

        /// <summary>
        /// Wait for channel's state to change. The task completes when <see cref="State"/> becomes different from <paramref name="lastObservedState"/>.
        /// </summary>
        /// <param name="lastObservedState">The last observed state. The task completes when <see cref="State"/> becomes different from this value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public Task WaitForStateChangedAsync(ConnectivityState lastObservedState, CancellationToken cancellationToken = default)
        {
            return ConnectionManager.WaitForStateChangedAsync(lastObservedState, waitForState: null, cancellationToken);
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

            lock (_lock)
            {
                if (ActiveCalls.Count > 0)
                {
                    // Disposing a call will remove it from ActiveCalls. Need to take a copy
                    // to avoid enumeration from being modified
                    var activeCallsCopy = ActiveCalls.ToArray();

                    foreach (var activeCall in activeCallsCopy)
                    {
                        activeCall.Dispose();
                    }
                }
            }

            if (_shouldDisposeHttpClient)
            {
                HttpInvoker.Dispose();
            }
            ConnectionManager.Dispose();
            Resolver.Dispose();
            Disposed = true;
        }

        internal bool TryAddToRetryBuffer(long messageSize)
        {
            lock (_lock)
            {
                if (CurrentRetryBufferSize + messageSize > MaxRetryBufferSize)
                {
                    return false;
                }

                CurrentRetryBufferSize += messageSize;
                return true;
            }
        }

        internal void RemoveFromRetryBuffer(long messageSize)
        {
            lock (_lock)
            {
                CurrentRetryBufferSize -= messageSize;
            }
        }

        internal int GetRandomNumber(int minValue, int maxValue)
        {
            CompatibilityHelpers.Assert(RandomGenerator != null);

            lock (_lock)
            {
                return RandomGenerator.Next(minValue, maxValue);
            }
        }

        /// <summary>
        /// Allows explicitly requesting channel to connect without starting an RPC.
        /// Returned task completes once <see cref="State"/> Ready was seen.
        /// There is no need to call this explicitly unless your use case requires that.
        /// Starting an RPC on a new channel will request connection implicitly.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            return ConnectionManager.ConnectAsync(waitForReady: true, cancellationToken);
        }

        ISubchannelTransport ISubchannelTransportFactory.Create(Subchannel subchannel)
        {
#if NET5_0_OR_GREATER
            var isTcpTransport = HttpHandlerType == HttpHandlerType.Default;

            if (isTcpTransport && SocketsHttpHandler.IsSupported)
            {
                return new ActiveSubchannelTransport(subchannel, TimeSpan.FromSeconds(5));
            }
#endif

            return new PassiveSubchannelTransport(subchannel);
        }

        private struct MethodKey : IEquatable<MethodKey>
        {
            public MethodKey(string? service, string? method)
            {
                Service = service;
                Method = method;
            }

            public string? Service { get; }
            public string? Method { get; }

            public override bool Equals(object? obj) => obj is MethodKey n ? Equals(n) : false;

            // Service and method names are case sensitive.
            public bool Equals(MethodKey other) => other.Service == Service && other.Method == Method;

            public override int GetHashCode() =>
                (Service != null ? StringComparer.Ordinal.GetHashCode(Service) : 0) ^
                (Method != null ? StringComparer.Ordinal.GetHashCode(Method) : 0);
        }

        private static class Log
        {
            private static readonly Action<ILogger, string, Exception?> _addressPathUnused =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, "AddressPathUnused"), "The path in the channel's address '{Address}' won't be used when making gRPC calls. A DelegatingHandler can be used to include a path with gRPC calls. See https://aka.ms/aspnet/grpc/subdir for details.");

            public static void AddressPathUnused(ILogger logger, string address)
            {
                _addressPathUnused(logger, address, null);
            }
        }
    }
}
