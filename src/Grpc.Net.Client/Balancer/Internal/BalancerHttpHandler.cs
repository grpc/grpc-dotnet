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

#if SUPPORT_LOAD_BALANCING
using System.Diagnostics;
using System.Net;
using Grpc.Shared;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer.Internal;

internal sealed partial class BalancerHttpHandler : DelegatingHandler
{
    private static readonly object SetupLock = new object();

    internal const string WaitForReadyKey = "WaitForReady";
    internal const string SubchannelKey = "Subchannel";
    internal const string CurrentAddressKey = "CurrentAddress";
    internal const string IsSocketsHttpHandlerSetupKey = "IsSocketsHttpHandlerSetup";

    private readonly ConnectionManager _manager;
    private readonly ILogger _logger;

    public BalancerHttpHandler(HttpMessageHandler innerHandler, ConnectionManager manager)
        : base(innerHandler)
    {
        _manager = manager;
        _logger = manager.LoggerFactory.CreateLogger(typeof(BalancerHttpHandler));
    }

    internal static bool IsSocketsHttpHandlerSetup(SocketsHttpHandler socketsHttpHandler)
    {
        lock (SetupLock)
        {
            return socketsHttpHandler.Properties.TryGetValue(IsSocketsHttpHandlerSetupKey, out var value) &&
                value is bool isEnabled &&
                isEnabled;
        }
    }

    internal static void ConfigureSocketsHttpHandlerSetup(
        SocketsHttpHandler socketsHttpHandler,
        Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> connectCallback)
    {
        // We're modifying the SocketsHttpHandler and nothing prevents two threads from creating a
        // channel with the same handler on different threads.
        // Place handler reads and modifications in a lock to ensure there is no chance of race conditions.
        // This is a static lock but it is only called once when a channel is created and the logic
        // inside it will complete straight away. Shouldn't have any performance impact.
        lock (SetupLock)
        {
            if (!IsSocketsHttpHandlerSetup(socketsHttpHandler))
            {
                Debug.Assert(socketsHttpHandler.ConnectCallback == null, "ConnectCallback should be null to get to this point.");

                socketsHttpHandler.ConnectCallback = connectCallback;
                socketsHttpHandler.Properties[IsSocketsHttpHandlerSetupKey] = true;
            }
        }
    }

#if NET5_0_OR_GREATER
    internal async ValueTask<Stream> OnConnect(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        Log.StartingConnectCallback(_logger, context.DnsEndPoint);

        if (!context.InitialRequestMessage.TryGetOption<Subchannel>(SubchannelKey, out var subchannel))
        {
            throw new InvalidOperationException($"Unable to get subchannel from {nameof(HttpRequestMessage)}.");
        }
        if (!context.InitialRequestMessage.TryGetOption<BalancerAddress>(CurrentAddressKey, out var currentAddress))
        {
            throw new InvalidOperationException($"Unable to get current address from {nameof(HttpRequestMessage)}.");
        }

        Debug.Assert(context.DnsEndPoint.Equals(currentAddress.EndPoint), "Context endpoint should equal address endpoint.");
        return await subchannel.Transport.GetStreamAsync(currentAddress.EndPoint, cancellationToken).ConfigureAwait(false);
    }
#endif

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri == null)
        {
            throw new InvalidOperationException("Request message URI not set.");
        }

        var waitForReady = false;
        if (request.TryGetOption<bool>(WaitForReadyKey, out var value))
        {
            waitForReady = value;
        }

        await _manager.ConnectAsync(waitForReady: false, cancellationToken).ConfigureAwait(false);
        var pickContext = new PickContext { Request = request };
        var result = await _manager.PickAsync(pickContext, waitForReady, cancellationToken).ConfigureAwait(false);
        var address = result.Address;
        var addressEndpoint = address.EndPoint;

        // Update request host if required.
        if (!request.RequestUri.IsAbsoluteUri ||
            request.RequestUri.Host != addressEndpoint.Host ||
            request.RequestUri.Port != addressEndpoint.Port)
        {
            var uriBuilder = new UriBuilder(request.RequestUri);
            uriBuilder.Host = addressEndpoint.Host;
            uriBuilder.Port = addressEndpoint.Port;
            request.RequestUri = uriBuilder.Uri;

            if (address.Attributes.TryGetValue(ConnectionManager.HostOverrideKey, out var hostOverride))
            {
                request.Headers.TryAddWithoutValidation("Host", hostOverride);
            }
        }

#if NET5_0_OR_GREATER
        // Set sub-connection onto request.
        // Will be used to get a stream in SocketsHttpHandler.ConnectCallback.
        request.SetOption(SubchannelKey, result.Subchannel);
        request.SetOption(CurrentAddressKey, address);
#endif

        Log.SendingRequest(_logger, request.RequestUri);
        var responseMessageTask = base.SendAsync(request, cancellationToken);
        result.SubchannelCallTracker?.Start();

        try
        {
            var responseMessage = await responseMessageTask.ConfigureAwait(false);

            // TODO(JamesNK): This doesn't take into account long running streams.
            // If there is response content then we need to wait until it is read to the end
            // or the request is disposed.
            result.SubchannelCallTracker?.Complete(new CompletionContext
            {
                Address = address
            });

            return responseMessage;
        }
        catch (Exception ex)
        {
            result.SubchannelCallTracker?.Complete(new CompletionContext
            {
                Address = address,
                Error = ex
            });

            throw;
        }
    }

    internal static partial class Log
    {

        [LoggerMessage(Level = LogLevel.Trace, EventId = 1, EventName = "SendingRequest", Message = "Sending request {RequestUri}.")]
        public static partial void SendingRequest(ILogger logger, Uri requestUri);

        [LoggerMessage(Level = LogLevel.Trace, EventId = 2, EventName = "StartingConnectCallback", Message = "Starting connect callback for {Endpoint}.")]
        private static partial void StartingConnectCallback(ILogger logger, string endpoint);

        public static void StartingConnectCallback(ILogger logger, DnsEndPoint endpoint)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                StartingConnectCallback(logger, $"{endpoint.Host}:{endpoint.Port}");
            }
        }
    }
}
#endif
