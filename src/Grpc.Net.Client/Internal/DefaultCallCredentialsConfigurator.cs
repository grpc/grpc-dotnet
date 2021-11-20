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

using Grpc.Core;

namespace Grpc.Net.Client.Internal
{
    internal class DefaultCallCredentialsConfigurator : CallCredentialsConfiguratorBase
    {
        public AsyncAuthInterceptor? Interceptor { get; private set; }
        public IReadOnlyList<CallCredentials>? Credentials { get; private set; }

        public void Reset()
        {
            Interceptor = null;
            Credentials = null;
        }

        public override void SetAsyncAuthInterceptorCredentials(object state, AsyncAuthInterceptor interceptor)
        {
            Interceptor = interceptor;
        }

        public override void SetCompositeCredentials(object state, IReadOnlyList<CallCredentials> credentials)
        {
            Credentials = credentials;
        }
    }
}
