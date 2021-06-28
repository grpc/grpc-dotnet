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

#if NET5_0_OR_GREATER

namespace Grpc.Core
{
    /// <summary>
    /// The connectivity state.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public enum ConnectivityState
    {
        /// <summary>
        /// Not trying to create a connection.
        /// </summary>
        Idle,
        /// <summary>
        /// Establishing a connection.
        /// </summary>
        Connecting,
        /// <summary>
        /// Connection ready.
        /// </summary>
        Ready,
        /// <summary>
        /// A transient failure on connection.
        /// </summary>
        TransientFailure,
        /// <summary>
        /// Connection shutdown.
        /// </summary>
        Shutdown
    }
}

#endif
