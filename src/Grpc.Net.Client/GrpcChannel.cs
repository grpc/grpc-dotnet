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
using GrpcServiceConfig = Grpc.Net.Client.Configuration.ServiceConfig;
using Grpc.Net.Compression;
using Grpc.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Grpc.Net.Client.Internal.Retry;
using System.Threading;
using System.Diagnostics;

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
        internal const int DefaultMaxRetryAttempts = 5;
        internal const long DefaultMaxRetryBufferSize = 1024 * 1024 * 16; // 16 MB
        internal const long DefaultMaxRetryBufferPerCallSize = 1024 * 1024; // 1 MB

        private readonly object _lock;
        private readonly ConcurrentDictionary<IMethod, GrpcMethodInfo> _methodInfoCache;
        private readonly Func<IMethod, GrpcMethodInfo> _createMethodInfoFunc;
        private readonly Dictionary<MethodKey, MethodConfig>? _serviceConfigMethods;
        private readonly Random? _random;
        // Internal for testing
        internal readonly HashSet<IDisposable> ActiveCalls;

        internal Uri Address { get; }
        internal HttpMessageInvoker HttpInvoker { get; }
        internal bool IsWinHttp { get; }
        internal int? SendMaxMessageSize { get; }
        internal int? ReceiveMaxMessageSize { get; }
        internal int? MaxRetryAttempts { get; }
        internal long? MaxRetryBufferSize { get; }
        internal long? MaxRetryBufferPerCallSize { get; }
        internal ILoggerFactory LoggerFactory { get; }
        internal ILogger Logger { get; }
        internal bool ThrowOperationCanceledOnCancellation { get; }
        internal bool? IsSecure { get; }
        internal List<CallCredentials>? CallCredentials { get; }
        internal Dictionary<string, ICompressionProvider> CompressionProviders { get; }
        internal string MessageAcceptEncoding { get; }
        internal bool Disposed { get; private set; }

        // Stateful
        internal ChannelRetryThrottling? RetryThrottling { get; }
        internal long CurrentRetryBufferSize;

        // Options that are set in unit tests
        internal ISystemClock Clock = SystemClock.Instance;
        internal IOperatingSystem OperatingSystem = Internal.OperatingSystem.Instance;
        internal bool DisableClientDeadline;
        internal long MaxTimerDueTime = uint.MaxValue - 1; // Max System.Threading.Timer due time

        private readonly bool _shouldDisposeHttpClient;

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
            HttpInvoker = channelOptions.HttpClient ?? CreateInternalHttpInvoker(channelOptions.HttpHandler);
            IsWinHttp = channelOptions.HttpHandler != null ? HttpHandlerFactory.HasHttpHandlerType(channelOptions.HttpHandler, "System.Net.Http.WinHttpHandler") : false;
            SendMaxMessageSize = channelOptions.MaxSendMessageSize;
            ReceiveMaxMessageSize = channelOptions.MaxReceiveMessageSize;
            MaxRetryAttempts = channelOptions.MaxRetryAttempts;
            MaxRetryBufferSize = channelOptions.MaxRetryBufferSize;
            MaxRetryBufferPerCallSize = channelOptions.MaxRetryBufferPerCallSize;
            CompressionProviders = ResolveCompressionProviders(channelOptions.CompressionProviders);
            MessageAcceptEncoding = GrpcProtocolHelpers.GetMessageAcceptEncoding(CompressionProviders);
            LoggerFactory = channelOptions.LoggerFactory ?? NullLoggerFactory.Instance;
            Logger = LoggerFactory.CreateLogger<GrpcChannel>();
            ThrowOperationCanceledOnCancellation = channelOptions.ThrowOperationCanceledOnCancellation;
            _createMethodInfoFunc = CreateMethodInfo;
            ActiveCalls = new HashSet<IDisposable>();
            if (channelOptions.ServiceConfig is { } serviceConfig)
            {
                RetryThrottling = serviceConfig.RetryThrottling != null ? CreateChannelRetryThrottling(serviceConfig.RetryThrottling) : null;
                _serviceConfigMethods = CreateServiceConfigMethods(serviceConfig);
                _random = new Random();
            }

            if (channelOptions.Credentials != null)
            {
                var configurator = new DefaultChannelCredentialsConfigurator();
                channelOptions.Credentials.InternalPopulateConfiguration(configurator, null);

                IsSecure = configurator.IsSecure;
                CallCredentials = configurator.CallCredentials;

                ValidateChannelCredentials();
            }

            if (!string.IsNullOrEmpty(Address.PathAndQuery) && Address.PathAndQuery != "/")
            {
                Log.AddressPathUnused(Logger, Address.OriginalString);
            }
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

        private static Dictionary<MethodKey, MethodConfig> CreateServiceConfigMethods(GrpcServiceConfig serviceConfig)
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

        private static HttpMessageInvoker CreateInternalHttpInvoker(HttpMessageHandler? handler)
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

            return new GrpcMethodInfo(scope, new Uri(Address, uri), methodConfig);
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

            if (string.IsNullOrEmpty(address.Host))
            {
                throw new ArgumentException($"Address '{address.OriginalString}' doesn't have a host. Address should include a scheme, host, and optional port. For example, 'https://localhost:5001'.");
            }

            if (channelOptions.HttpClient != null && channelOptions.HttpHandler != null)
            {
                throw new ArgumentException($"{nameof(GrpcChannelOptions.HttpClient)} and {nameof(GrpcChannelOptions.HttpHandler)} have been configured. " +
                    $"Only one HTTP caller can be specified.");
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
            CompatibilityHelpers.Assert(_random != null);

            lock (_lock)
            {
                return _random.Next(minValue, maxValue);
            }
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
