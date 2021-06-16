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
using System.Diagnostics;
using System.Net;
using Grpc.Core;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// A balancing decision made by a <see cref="SubchannelPicker"/> for a gRPC call.
    /// </summary>
#if HAVE_LOAD_BALANCING
    public
#else
    internal
#endif
        sealed class PickResult
    {
        private readonly Action<CompleteContext>? _onComplete;

        [DebuggerStepThrough]
        private PickResult(PickResultType pickResultType, Subchannel? subchannel, Status status, Action<CompleteContext>? onComplete)
        {
            Type = pickResultType;
            Subchannel = subchannel;
            Status = status;
            _onComplete = onComplete;
        }

        /// <summary>
        /// The pick result type.
        /// </summary>
        public PickResultType Type { get; }

        /// <summary>
        /// The <see cref="Subchannel"/> provided by <see cref="ForComplete(Subchannel, Action{CompleteContext}?)"/>.
        /// </summary>
        public Subchannel? Subchannel { get; }

        /// <summary>
        /// The <see cref="Grpc.Core.Status"/> provided by <see cref="ForFail(Status)"/> or <see cref="ForDrop(Status)"/>.
        /// </summary>
        public Status Status { get; }

        /// <summary>
        /// Called to notify the load balancer that a call is complete.
        /// </summary>
        /// <param name="context">The complete context.</param>
        public void Complete(CompleteContext context)
        {
            _onComplete?.Invoke(context);
            Subchannel?.Transport.OnRequestComplete(context);
        }

        /// <summary>
        /// Create a <see cref="PickResult"/> that provides a <see cref="Balancer.Subchannel"/> to gRPC calls.
        /// <para>
        /// A result created with a <see cref="Balancer.Subchannel"/> won't necessarily be used by a gRPC call.
        /// The subchannel's state may change at the same time the picker is making a decision. That means the
        /// decision may be made with outdated information. For example, a picker may return a subchannel
        /// with a <see cref="Subchannel.State"/> that is <see cref="ConnectivityState.Ready"/>, but
        /// becomes <see cref="ConnectivityState.Idle"/> when the subchannel is about to be used. In that situation
        /// the gRPC call waits for the load balancer to react to the new state and create a new picker.
        /// </para>
        /// </summary>
        /// <param name="subchannel">The picked subchannel.</param>
        /// <param name="onComplete">An optional callback to be notified of a call being completed.</param>
        /// <returns>The pick result.</returns>
        [DebuggerStepThrough]
        public static PickResult ForComplete(Subchannel subchannel, Action<CompleteContext>? onComplete = null)
        {
            return new PickResult(PickResultType.Complete, subchannel, Status.DefaultSuccess, onComplete);
        }

        /// <summary>
        /// Creates a <see cref="PickResult"/> to report a connectivity error to calls. If the call has
        /// a <see cref="CallOptions.IsWaitForReady"/> value of <c>true</c> then the call will wait.
        /// </summary>
        /// <param name="status">The error status. Must not be <see cref="StatusCode.OK"/>.</param>
        /// <returns>The pick result.</returns>
        [DebuggerStepThrough]
        public static PickResult ForFail(Status status)
        {
            return new PickResult(PickResultType.Fail, subchannel: null, status, onComplete: null);
        }

        /// <summary>
        /// Creates a <see cref="PickResult"/> to fail a gRPC call immediately. A result with a type of 
        /// <see cref="PickResultType.Drop"/> causes calls to ignore <see cref="CallOptions.IsWaitForReady"/> and retry.
        /// </summary>
        /// <param name="status">The error status. Must not be <see cref="StatusCode.OK"/>.</param>
        /// <returns>The pick result.</returns>
        [DebuggerStepThrough]
        public static PickResult ForDrop(Status status)
        {
            return new PickResult(PickResultType.Drop, subchannel: null, status, onComplete: null);
        }

        /// <summary>
        /// Creates a <see cref="PickResult"/> to queue gRPC calls.
        /// </summary>
        /// <returns>The pick result.</returns>
        [DebuggerStepThrough]
        public static PickResult ForQueue()
        {
            return new PickResult(PickResultType.Queue, subchannel: null, Status.DefaultSuccess, onComplete: null);
        }
    }

    /// <summary>
    /// The <see cref="PickResult"/> type.
    /// </summary>
#if HAVE_LOAD_BALANCING
    public
#else
    internal
#endif
        enum PickResultType
    {
        /// <summary>
        /// Result with a <see cref="Subchannel"/>.
        /// </summary>
        Complete,
        /// <summary>
        /// Result with no result. Queue gRPC calls.
        /// </summary>
        Queue,
        /// <summary>
        /// Result with a connectivity error. <see cref="CallOptions.IsWaitForReady"/> will queue gRPC calls.
        /// </summary>
        Fail,
        /// <summary>
        /// Result with an immediate failure. <see cref="CallOptions.IsWaitForReady"/> and retry are ignored.
        /// </summary>
        Drop
    }
}
