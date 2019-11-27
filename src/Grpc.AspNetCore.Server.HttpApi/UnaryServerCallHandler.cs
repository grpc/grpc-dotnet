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
using System.IO;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Shared.Server;
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Server.HttpApi
{
    internal class UnaryServerCallHandler<TService, TRequest, TResponse>
        where TService : class
        where TRequest : class
        where TResponse : class
    {
        private readonly UnaryServerMethodInvoker<TService, TRequest, TResponse> _unaryMethodInvoker;
        private readonly FieldDescriptor? _responseBodyDescriptor;
        private readonly MessageDescriptor? _bodyDescriptor;
        private readonly FieldDescriptor? _bodyFieldDescriptor;
        private readonly Dictionary<string, List<FieldDescriptor>> _routeParameterDescriptors;
        private readonly ConcurrentDictionary<string, List<FieldDescriptor>> _queryParameterDescriptors;

        public UnaryServerCallHandler(
            UnaryServerMethodInvoker<TService, TRequest, TResponse> unaryMethodInvoker,
            FieldDescriptor? responseBodyDescriptor,
            MessageDescriptor? bodyDescriptor,
            FieldDescriptor? bodyFieldDescriptor,
            Dictionary<string, List<FieldDescriptor>> routeParameterDescriptors)
        {
            _unaryMethodInvoker = unaryMethodInvoker;
            _responseBodyDescriptor = responseBodyDescriptor;
            _bodyDescriptor = bodyDescriptor;
            _bodyFieldDescriptor = bodyFieldDescriptor;
            _routeParameterDescriptors = routeParameterDescriptors;
            _queryParameterDescriptors = new ConcurrentDictionary<string, List<FieldDescriptor>>(StringComparer.Ordinal);
        }

        public async Task HandleCallAsync(HttpContext httpContext)
        {
            var requestMessage = await CreateMessage(httpContext);

            var serverCallContext = new HttpApiServerCallContext();

            var response = await _unaryMethodInvoker.Invoke(httpContext, serverCallContext, (TRequest)requestMessage);

            await SendResponse(httpContext, response);
        }

        private async Task SendResponse(HttpContext httpContext, TResponse response)
        {
            object responseBody = response;

            if (_responseBodyDescriptor != null)
            {
                responseBody = _responseBodyDescriptor.Accessor.GetValue((IMessage)responseBody);
            }

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/json";

            var ms = new MemoryStream();
            using (var writer = new StreamWriter(ms, leaveOpen: true))
            {
                if (responseBody is IMessage responseMessage)
                {
                    JsonFormatter.Default.Format(responseMessage, writer);
                }
                else
                {
                    JsonFormatter.Default.WriteValue(writer, responseBody);
                }

                writer.Flush();
            }
            ms.Seek(0, SeekOrigin.Begin);

            await ms.CopyToAsync(httpContext.Response.Body);
            await httpContext.Response.Body.FlushAsync();
        }

        private bool CanBindQueryStringVariable(string variable)
        {
            if (_bodyDescriptor != null)
            {
                if (_bodyFieldDescriptor == null)
                {
                    return false;
                }

                if (variable == _bodyFieldDescriptor.Name)
                {
                    return false;
                }

                var separator = variable.IndexOf('.', StringComparison.Ordinal);
                if (separator > -1)
                {
                    if (variable.AsSpan(0, separator).Equals(_bodyFieldDescriptor.Name.AsSpan(), StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            if (_routeParameterDescriptors.ContainsKey(variable))
            {
                return false;
            }

            return true;
        }

        private async Task<IMessage> CreateMessage(HttpContext httpContext)
        {
            IMessage? requestMessage;

            if (_bodyDescriptor != null)
            {
                if (string.Equals(httpContext.Request.ContentType, "application/json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Request content-type of application/json is required.");
                }

                var ms = new MemoryStream();
                await httpContext.Request.Body.CopyToAsync(ms);
                ms.Seek(0, SeekOrigin.Begin);

                var bodyContent = JsonParser.Default.Parse(new StreamReader(ms), _bodyDescriptor);
                if (_bodyFieldDescriptor != null)
                {
                    requestMessage = (IMessage)Activator.CreateInstance<TRequest>();
                    _bodyFieldDescriptor.Accessor.SetValue(requestMessage, bodyContent);
                }
                else
                {
                    requestMessage = bodyContent;
                }
            }
            else
            {
                requestMessage = (IMessage)Activator.CreateInstance<TRequest>();
            }

            foreach (var parameterDescriptor in _routeParameterDescriptors)
            {
                var routeValue = httpContext.Request.RouteValues[parameterDescriptor.Key];
                if (routeValue != null)
                {
                    ServiceDescriptorHelpers.RecursiveSetValue(requestMessage, parameterDescriptor.Value, routeValue);
                }
            }

            foreach (var item in httpContext.Request.Query)
            {
                if (CanBindQueryStringVariable(item.Key))
                {
                    if (!_queryParameterDescriptors.TryGetValue(item.Key, out var pathDescriptors))
                    {
                        if (ServiceDescriptorHelpers.TryResolveDescriptors(requestMessage.Descriptor, item.Key, out pathDescriptors))
                        {
                            _queryParameterDescriptors[item.Key] = pathDescriptors;
                        }
                    }

                    if (pathDescriptors != null)
                    {
                        object value = item.Value.Count == 1 ? (object)item.Value[0] : item.Value;
                        ServiceDescriptorHelpers.RecursiveSetValue(requestMessage, pathDescriptors, value);
                    }
                }
            }

            return requestMessage;
        }
    }
}
