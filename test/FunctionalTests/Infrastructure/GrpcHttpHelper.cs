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
using System.Net.Http;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public static class GrpcHttpHelper
    {
        public static HttpRequestMessage Create(string url, HttpMethod? method = null)
        {
            var request = new HttpRequestMessage(method ?? HttpMethod.Post, url);
            request.Version = new Version(2, 0);
#if NET5_0_OR_GREATER
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
#endif

            return request;
        }
    }
}
