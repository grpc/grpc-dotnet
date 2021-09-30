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

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Grpc.Net.ClientFactory.Internal
{
    // Thread-safety: This class is immutable
    internal sealed class ExpiredChannelTrackingEntry
    {
        private readonly WeakReference _livenessTracker;

        // IMPORTANT: don't cache a reference to `other` or `other.CallInvoker` here.
        // We need to allow it to be GC'ed.
        public ExpiredChannelTrackingEntry(ActiveChannelTrackingEntry other)
        {
            Key = other.Key;
            Scope = other.Scope;

            _livenessTracker = new WeakReference(other.CallInvoker);
            InnerInvoker = other.CallInvoker.InnerInvoker;
            Channel = other.CallInvoker.Channel;
        }

        public bool CanDispose => !_livenessTracker.IsAlive;

        public CallInvoker InnerInvoker { get; }

        public GrpcChannel Channel { get; }

        public EntryKey Key { get; }

        public IServiceScope? Scope { get; }
    }
}
