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

using System.Threading.Tasks;
using FunctionalTestsWebsite.Infrastructure;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Lifetime;

namespace FunctionalTestsWebsite.Services
{
    public class LifetimeService : Lifetime.LifetimeService.LifetimeServiceBase
    {
        private readonly SingletonValueProvider _singletonValueProvider;
        private readonly TransientValueProvider _transientValueProvider;
        private readonly ScopedValueProvider _scopedValueProvider;

        public LifetimeService(SingletonValueProvider singletonValueProvider, TransientValueProvider transientValueProvider, ScopedValueProvider scopedValueProvider)
        {
            _singletonValueProvider = singletonValueProvider;
            _transientValueProvider = transientValueProvider;
            _scopedValueProvider = scopedValueProvider;
        }

        public override Task<ValueResponse> GetSingletonValue(Empty request, ServerCallContext context)
        {
            return Task.FromResult(CreateValueResponse(_singletonValueProvider));
        }

        public override Task<ValueResponse> GetScopedValue(Empty request, ServerCallContext context)
        {
            return Task.FromResult(CreateValueResponse(_scopedValueProvider));
        }

        public override Task<ValueResponse> GetTransientValue(Empty request, ServerCallContext context)
        {
            return Task.FromResult(CreateValueResponse(_transientValueProvider));
        }

        private ValueResponse CreateValueResponse(ValueProvider valueProvider)
        {
            return new ValueResponse { Value = valueProvider.GetNext() };
        }
    }
}
