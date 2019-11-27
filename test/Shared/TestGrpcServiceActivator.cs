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
using System.Threading.Tasks;
using Grpc.AspNetCore.Server;

namespace Grpc.Tests.Shared
{
    internal class TestGrpcServiceActivator<TGrpcService> : IGrpcServiceActivator<TGrpcService> where TGrpcService : class, new()
    {
        public GrpcActivatorHandle<TGrpcService> Create(IServiceProvider serviceProvider)
        {
            return new GrpcActivatorHandle<TGrpcService>(new TGrpcService(), false, null);
        }

        public ValueTask ReleaseAsync(GrpcActivatorHandle<TGrpcService> service)
        {
            return default;
        }
    }
}