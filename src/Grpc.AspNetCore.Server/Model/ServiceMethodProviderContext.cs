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

using System.Collections.Generic;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Model.Internal;
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Model
{
    /// <summary>
    /// 
    /// </summary>
    public class ServiceMethodProviderContext<TService> where TService : class
    {
        private readonly ServerCallHandlerFactory<TService> _serverCallHandlerFactory;

        internal ServiceMethodProviderContext(ServerCallHandlerFactory<TService> serverCallHandlerFactory)
        {
            Methods = new List<MethodModel>();
            _serverCallHandlerFactory = serverCallHandlerFactory;
        }

        internal List<MethodModel> Methods { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <param name="metadata"></param>
        /// <param name="invoker"></param>
        public void AddUnaryMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, IList<object> metadata, UnaryServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            var callHandler = _serverCallHandlerFactory.CreateUnary<TRequest, TResponse>(method, invoker);
            var methodModel = new MethodModel(method, metadata, callHandler.HandleCallAsync);

            Methods.Add(methodModel);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <param name="metadata"></param>
        /// <param name="invoker"></param>
        public void AddServerStreamingMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, List<object> metadata, ServerStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            var callHandler = _serverCallHandlerFactory.CreateServerStreaming<TRequest, TResponse>(method, invoker);
            var methodModel = new MethodModel(method, metadata, callHandler.HandleCallAsync);

            Methods.Add(methodModel);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <param name="metadata"></param>
        /// <param name="invoker"></param>
        public void AddClientStreamingMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, List<object> metadata, ClientStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            var callHandler = _serverCallHandlerFactory.CreateClientStreaming<TRequest, TResponse>(method, invoker);
            var methodModel = new MethodModel(method, metadata, callHandler.HandleCallAsync);

            Methods.Add(methodModel);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <param name="metadata"></param>
        /// <param name="invoker"></param>
        public void AddDuplexStreamingMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, List<object> metadata, DuplexStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            var callHandler = _serverCallHandlerFactory.CreateDuplexStreaming<TRequest, TResponse>(method, invoker);
            var methodModel = new MethodModel(method, metadata, callHandler.HandleCallAsync);

            Methods.Add(methodModel);
        }
    }
}
