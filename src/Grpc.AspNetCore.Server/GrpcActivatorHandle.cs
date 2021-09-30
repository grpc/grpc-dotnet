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

namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// Handle to the activator instance.
    /// </summary>
    /// <typeparam name="T">The instance type.</typeparam>
    public readonly struct GrpcActivatorHandle<T>
    {
        /// <summary>
        /// Creates a new instance of <see cref="GrpcActivatorHandle{T}"/>.
        /// </summary>
        /// <param name="instance">The activated instance.</param>
        /// <param name="created">A value indicating whether the instance was created by the activator.</param>
        /// <param name="state">State related to the instance.</param>
        public GrpcActivatorHandle(T instance, bool created, object? state)
        {
            Instance = instance;
            Created = created;
            State = state;
        }

        /// <summary>
        /// Gets the activated instance.
        /// </summary>
        public T Instance { get; }

        /// <summary>
        /// Gets a value indicating whether the instanced was created by the activator.
        /// </summary>
        public bool Created { get; }

        /// <summary>
        /// Gets state related to the instance.
        /// </summary>
        public object? State { get; }
    }
}
