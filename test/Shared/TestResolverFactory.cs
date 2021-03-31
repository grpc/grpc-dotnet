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
using Grpc.Net.Client.Balancer;

namespace Grpc.Tests.Shared
{
    internal class TestResolverFactory : ResolverFactory
    {
        private readonly Func<Uri, ResolverOptions, TestResolver> _createResolver;

        public override string Name { get; } = "test";

        public TestResolverFactory(TestResolver resolver)
        {
            _createResolver = (address, options) => resolver;
        }

        public TestResolverFactory(Func<Uri, ResolverOptions, TestResolver> createResolver)
        {
            _createResolver = createResolver;
        }

        public override Resolver Create(Uri address, ResolverOptions options)
        {
            return _createResolver(address, options);
        }
    }
}
