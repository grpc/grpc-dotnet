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
using System;
using System.Net;
using Grpc.Core;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Context used to signal a call is complete.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public sealed class CompletionContext
    {
        /// <summary>
        /// Gets or sets the <see cref="BalancerAddress"/> a call was made with. Required.
        /// </summary>
        public BalancerAddress? Address { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="Exception"/> thrown when making the call.
        /// </summary>
        public Exception? Error { get; set; }
    }
}
#endif
