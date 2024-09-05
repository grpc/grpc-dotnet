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

using System.Diagnostics.CodeAnalysis;
using Grpc.Core.Interceptors;
using Grpc.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.AspNetCore.Server;

/// <summary>
/// Representation of a registration of an <see cref="Interceptor"/> in the server pipeline.
/// </summary>
public class InterceptorRegistration
{
    internal const DynamicallyAccessedMemberTypes InterceptorAccessibility = DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods;

    internal object[] _args;

    internal InterceptorRegistration([DynamicallyAccessedMembers(InterceptorAccessibility)]Type type, object[] arguments)
    {
        ArgumentNullThrowHelper.ThrowIfNull(type);
        ArgumentNullThrowHelper.ThrowIfNull(arguments);

        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == null)
            {
                throw new ArgumentException("Interceptor arguments contains a null value. Null interceptor arguments are not supported.", nameof(arguments));
            }
        }

        Type = type;
        _args = arguments;
    }

    /// <summary>
    /// Get the type of the interceptor.
    /// </summary>
    [DynamicallyAccessedMembers(InterceptorAccessibility)]
    public Type Type { get; }

    /// <summary>
    /// Get the arguments used to create the interceptor.
    /// </summary>
    public IReadOnlyList<object> Arguments => _args;

    private IGrpcInterceptorActivator? _interceptorActivator;
    private ObjectFactory? _factory;

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
        Justification = "Type parameter members are preserved with DynamicallyAccessedMembers on InterceptorRegistration.Type property.")]
    [UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode",
        Justification = "Type definition is explicitly specified and type argument is always an Interceptor type.")]
    internal IGrpcInterceptorActivator GetActivator(IServiceProvider serviceProvider)
    {
        // Not thread safe. Side effect is resolving the service twice.
        if (_interceptorActivator == null)
        {
            _interceptorActivator = (IGrpcInterceptorActivator)serviceProvider.GetRequiredService(typeof(IGrpcInterceptorActivator<>).MakeGenericType(Type));
        }

        return _interceptorActivator;
    }

    internal ObjectFactory GetFactory()
    {
        // Not thread safe. Side effect is resolving the factory twice.
        if (_factory == null)
        {
            _factory = ActivatorUtilities.CreateFactory(Type, _args.Select(a => a.GetType()).ToArray());
        }

        return _factory;
    }
}
