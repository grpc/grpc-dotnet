#region Copyright notice and license

// Copyright 2018 gRPC authors.
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

namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// Representation of a registration of the interceptor in the pipeline.
    /// </summary>
    public class InterceptorRegistration
    {
        internal InterceptorRegistration(Type type, object[] args)
        {
            Type = type;
            ActivatorType = typeof(IGrpcInterceptorActivator<>).MakeGenericType(Type);
            Args = args;
        }

        /// <summary>
        /// The type of the interceptor.
        /// </summary>
        public Type Type { get; }

        internal Type ActivatorType { get; }

        internal object[] Args { get; }
    }
}
