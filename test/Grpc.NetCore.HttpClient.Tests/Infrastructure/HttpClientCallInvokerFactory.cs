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

using Microsoft.Extensions.Logging;

namespace Grpc.NetCore.HttpClient.Tests.Infrastructure
{
    internal static class HttpClientCallInvokerFactory
    {
        public static HttpClientCallInvoker Create(System.Net.Http.HttpClient httpClient, ILoggerFactory? loggerFactory = null)
        {
            return new HttpClientCallInvoker(httpClient, loggerFactory);
        }
    }
}
