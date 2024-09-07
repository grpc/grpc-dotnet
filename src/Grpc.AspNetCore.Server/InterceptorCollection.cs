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

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Grpc.Core.Interceptors;
using Grpc.Shared;

namespace Grpc.AspNetCore.Server;

/// <summary>
/// Represents the pipeline of interceptors to be invoked when processing a gRPC call.
/// </summary>
public class InterceptorCollection : Collection<InterceptorRegistration>
{
    /// <summary>
    /// Add an interceptor to the end of the pipeline.
    /// </summary>
    /// <typeparam name="TInterceptor">The interceptor type.</typeparam>
    /// <param name="args">The list of arguments to pass to the interceptor constructor when creating an instance.</param>
    public void Add<[DynamicallyAccessedMembers(InterceptorRegistration.InterceptorAccessibility)] TInterceptor>(params object[] args) where TInterceptor : Interceptor
    {
        Add(typeof(TInterceptor), args);
    }

    /// <summary>
    /// Add an interceptor to the end of the pipeline.
    /// </summary>
    /// <param name="interceptorType">The interceptor type.</param>
    /// <param name="args">The list of arguments to pass to the interceptor constructor when creating an instance.</param>
    public void Add([DynamicallyAccessedMembers(InterceptorRegistration.InterceptorAccessibility)] Type interceptorType, params object[] args)
    {
        ArgumentNullThrowHelper.ThrowIfNull(interceptorType);

        if (!interceptorType.IsSubclassOf(typeof(Interceptor)))
        {
            throw new ArgumentException($"Type must inherit from {typeof(Interceptor).FullName}.", nameof(interceptorType));
        }

        Add(new InterceptorRegistration(interceptorType, args));
    }

    /// <summary>
    /// Append a set of interceptor registrations to the end of the pipeline.
    /// </summary>
    /// <param name="registrations">The set of interceptor registrations to add.</param>
    internal void AddRange(IEnumerable<InterceptorRegistration> registrations)
    {
        ArgumentNullThrowHelper.ThrowIfNull(registrations);

        foreach (var interceptorRegistration in registrations)
        {
            Add(interceptorRegistration);
        }
    }
}
