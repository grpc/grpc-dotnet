﻿#region Copyright notice and license

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
using System.Linq.Expressions;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Grpc.NetCore.HttpClient
{
    /// <summary>
    /// Factory for creating gRPC clients that will use HttpClient to make gRPC calls.
    /// </summary>
    public static class GrpcClientFactory
    {
        /// <summary>
        /// Creates a gRPC client using the specified address.
        /// </summary>
        /// <typeparam name="TClient">The type of the gRPC client. This type will typically be defined using generated code from a *.proto file.</typeparam>
        /// <param name="baseAddress">The base address to use when making gRPC requests.</param>
        /// <param name="certificate">The client certificate to use when making gRPC requests.</param>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory"/>.</param>
        /// <returns>A gRPC client.</returns>
        public static TClient Create<TClient>(string baseAddress, X509Certificate certificate = null, ILoggerFactory loggerFactory = null) where TClient : ClientBase<TClient>
        {
            var httpClientHandler = new HttpClientHandler();
            httpClientHandler.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true;
            if (certificate != null)
            {
                httpClientHandler.ClientCertificates.Add(certificate);
            }

            return CreateCore<TClient>(baseAddress, httpClientHandler, loggerFactory);
        }

        /// <summary>
        /// Creates a gRPC client using the specified address and handler.
        /// </summary>
        /// <typeparam name="TClient">The type of the gRPC client. This type will typically be defined using generated code from a *.proto file.</typeparam>
        /// <param name="baseAddress">The base address to use when making gRPC requests.</param>
        /// <param name="httpClientHandler">The <see cref="HttpClientHandler"/>.</param>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory"/>.</param>
        /// <returns>A gRPC client.</returns>
        public static TClient Create<TClient>(string baseAddress, HttpClientHandler httpClientHandler, ILoggerFactory loggerFactory) where TClient : ClientBase<TClient>
        {
            return CreateCore<TClient>(baseAddress, httpClientHandler, loggerFactory);
        }

        private static TClient CreateCore<TClient>(string baseAddress, HttpClientHandler httpClientHandler, ILoggerFactory loggerFactory) where TClient : ClientBase<TClient>
        {
            var httpClient = new System.Net.Http.HttpClient(httpClientHandler);
            httpClient.BaseAddress = new Uri(baseAddress, UriKind.RelativeOrAbsolute);

            return Cache<TClient>.Instance.Activator(new HttpClientCallInvoker(httpClient, loggerFactory));
        }

        private class Cache<TClient>
        {
            public static readonly Cache<TClient> Instance = new Cache<TClient>();

            private readonly static Func<Func<CallInvoker, TClient>> _createActivator = () =>
            {
                var constructor = typeof(TClient).GetConstructor(new[] { typeof(CallInvoker) });
                var callInvokerArgument = Expression.Parameter(typeof(CallInvoker), "arg");

                var activator = Expression.Lambda<Func<CallInvoker, TClient>>(
                    Expression.New(constructor, callInvokerArgument),
                    callInvokerArgument).Compile();

                return activator;
            };

            private Func<CallInvoker, TClient> _activator;
            private bool _initialized;
            private object _lock;

            public Func<CallInvoker, TClient> Activator => LazyInitializer.EnsureInitialized(
                ref _activator,
                ref _initialized,
                ref _lock,
                _createActivator);
        }
    }
}
