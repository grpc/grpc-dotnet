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

#if SUPPORT_LOAD_BALANCING
namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Base type for picking a subchannel. A <see cref="SubchannelPicker"/> is responsible for picking
    /// a ready <see cref="Subchannel"/> that gRPC calls will use.
    /// <para>
    /// Load balancers implement <see cref="SubchannelPicker"/> with their own balancing logic to
    /// determine which subchannel is returned for a call.
    /// </para>
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public abstract class SubchannelPicker
    {
        /// <summary>
        /// Picks a ready <see cref="Subchannel"/> for the specified context.
        /// </summary>
        /// <param name="context">The pick content.</param>
        /// <returns>A ready <see cref="Subchannel"/>.</returns>
        public abstract PickResult Pick(PickContext context);
    }
}
#endif
