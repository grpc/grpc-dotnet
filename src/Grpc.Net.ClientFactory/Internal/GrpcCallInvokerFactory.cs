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

using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.Net.ClientFactory.Internal;

internal readonly record struct EntryKey(string Name, Type Type);

internal class GrpcCallInvokerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptionsMonitor<GrpcClientFactoryOptions> _grpcClientFactoryOptionsMonitor;
    private readonly IOptionsMonitor<HttpClientFactoryOptions> _httpClientFactoryOptionsMonitor;
    private readonly IHttpMessageHandlerFactory _messageHandlerFactory;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<EntryKey, CallInvoker> _activeChannels;
    private readonly Func<EntryKey, CallInvoker> _invokerFactory;

    public GrpcCallInvokerFactory(
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
        IOptionsMonitor<GrpcClientFactoryOptions> grpcClientFactoryOptionsMonitor,
        IOptionsMonitor<HttpClientFactoryOptions> httpClientFactoryOptionsMonitor,
        IHttpMessageHandlerFactory messageHandlerFactory)
    {
        ArgumentNullThrowHelper.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _grpcClientFactoryOptionsMonitor = grpcClientFactoryOptionsMonitor;
        _httpClientFactoryOptionsMonitor = httpClientFactoryOptionsMonitor;
        _messageHandlerFactory = messageHandlerFactory;

        _scopeFactory = scopeFactory;
        _activeChannels = new ConcurrentDictionary<EntryKey, CallInvoker>();
        _invokerFactory = CreateInvoker;
    }

    public CallInvoker CreateInvoker(string name, Type type)
    {
        return _activeChannels.GetOrAdd(new EntryKey(name, type), _invokerFactory);
    }

    private CallInvoker CreateInvoker(EntryKey key)
    {
        var (name, type) = (key.Name, key.Type);
        var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            var httpClientFactoryOptions = _httpClientFactoryOptionsMonitor.Get(name);
            if (httpClientFactoryOptions.HttpClientActions.Count > 0)
            {
                throw new InvalidOperationException($"The ConfigureHttpClient method is not supported when creating gRPC clients. Unable to create client with name '{name}'.");
            }

            var clientFactoryOptions = _grpcClientFactoryOptionsMonitor.Get(name);
            var httpHandler = _messageHandlerFactory.CreateHandler(name);
            if (httpHandler == null)
            {
                throw new ArgumentNullException(nameof(httpHandler));
            }

            var channelOptions = new GrpcChannelOptions();
            channelOptions.HttpHandler = httpHandler;
            channelOptions.LoggerFactory = _loggerFactory;
            channelOptions.ServiceProvider = services;

            if (clientFactoryOptions.ChannelOptionsActions.Count > 0)
            {
                foreach (var applyOptions in clientFactoryOptions.ChannelOptionsActions)
                {
                    applyOptions(channelOptions);
                }
            }

            var address = clientFactoryOptions.Address;
            if (address == null)
            {
                throw new InvalidOperationException($@"Could not resolve the address for gRPC client '{name}'. Set an address when registering the client: services.AddGrpcClient<{type.Name}>(o => o.Address = new Uri(""https://localhost:5001""))");
            }

            if (clientFactoryOptions.HasCallCredentials && !AreCallCredentialsSupported(channelOptions, address))
            {
                // Throw error to tell dev that call credentials will never be used.
                throw new InvalidOperationException(
                    $"Call credential configured for gRPC client '{name}' requires TLS, and the client isn't configured to use TLS. " +
                    $"Either configure a TLS address, or use the call credential without TLS by setting {nameof(GrpcChannelOptions)}.{nameof(GrpcChannelOptions.UnsafeUseInsecureChannelCallCredentials)} to true: " +
                    @"client.AddCallCredentials((context, metadata) => {}).ConfigureChannel(o => o.UnsafeUseInsecureChannelCallCredentials = true)");
            }

            var channel = GrpcChannel.ForAddress(address, channelOptions);

            var httpClientCallInvoker = channel.CreateCallInvoker();

            var resolvedCallInvoker = GrpcClientFactoryOptions.BuildInterceptors(
                httpClientCallInvoker,
                services,
                clientFactoryOptions,
                InterceptorScope.Channel);

            return resolvedCallInvoker;
        }
        catch
        {
            // If something fails while creating the handler, dispose the services.
            scope?.Dispose();
            throw;
        }
    }

    private static bool AreCallCredentialsSupported(GrpcChannelOptions channelOptions, Uri address)
    {
        bool isSecure;

        if (address.Scheme == Uri.UriSchemeHttps)
        {
            isSecure = true;
        }
        else if (address.Scheme == Uri.UriSchemeHttp)
        {
            isSecure = false;
        }
        else
        {
            // Load balancing means the address won't have a standard scheme, e.g. dns:///
            // Use call credentials to figure out whether the channel is secure.
            isSecure = HasSecureCredentials(channelOptions.Credentials);
        }

        return isSecure || channelOptions.UnsafeUseInsecureChannelCallCredentials;

        static bool HasSecureCredentials(ChannelCredentials? channelCredentials)
        {
            if (channelCredentials == null)
            {
                return false;
            }
            if (channelCredentials is SslCredentials)
            {
                return true;
            }

            var configurator = new ClientFactoryCredentialsConfigurator();
            channelCredentials.InternalPopulateConfiguration(configurator, channelCredentials);

            return configurator.IsSecure ?? false;
        }
    }

    private sealed class ClientFactoryCredentialsConfigurator : ChannelCredentialsConfiguratorBase
    {
        public bool? IsSecure { get; private set; }

        public override void SetInsecureCredentials(object state)
        {
            IsSecure = false;
        }

        public override void SetSslCredentials(object state, string? rootCertificates, KeyCertificatePair? keyCertificatePair, VerifyPeerCallback? verifyPeerCallback)
        {
            IsSecure = true;
        }

        public override void SetCompositeCredentials(object state, ChannelCredentials channelCredentials, CallCredentials callCredentials)
        {
        }
    }
}
