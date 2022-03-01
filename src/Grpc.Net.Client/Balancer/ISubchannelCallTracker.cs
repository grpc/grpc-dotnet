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
    /// An interface for tracking subchannel calls.
    /// </summary>
    public interface ISubchannelCallTracker
    {
        /// <summary>
        /// Called when a subchannel call is started after a load balance pick.
        /// </summary>
        void Start();

        /// <summary>
        /// Called when a subchannel call is completed.
        /// </summary>
        /// <param name="context">The complete context.</param>
        void Complete(CompletionContext context);
    }
}
#endif
