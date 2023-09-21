#region Copyright notice and license

// Copyright 2015-2016 gRPC authors.
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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Grpc.Core.Internal;

internal static class ClientDebuggerHelpers
{
    private static Type? GetParentType(Type clientType)
    {
        // Attempt to get the parent type for a generated client.
        // A generated client is always nested inside a static type that contains information about the client.
        // For example:
        //
        // public static class Greeter
        // {
        //     private static readonly serviceName = "Greeter";
        //     private static readonly Method<HelloRequest, HelloReply> _sayHelloMethod;
        //
        //     public class GreeterClient { }
        // }

        if (!clientType.IsNested)
        {
            return null;
        }

        var parentType = clientType.DeclaringType;
        // Check parent type is static. A C# static type is sealed and abstract.
        if (parentType == null || (!parentType.IsSealed && !parentType.IsAbstract))
        {
            return null;
        }

        return parentType;
    }

    [UnconditionalSuppressMessage("Trimmer", "IL2075", Justification = "Only used by debugging. If trimming is enabled then missing data is not displayed in debugger.")]
    internal static string? GetServiceName(Type clientType)
    {
        // Attempt to get the service name from the generated __ServiceName field.
        // If the service name can't be resolved then it isn't displayed in the client's debugger display.
        var parentType = GetParentType(clientType);
        var field = parentType?.GetField("__ServiceName", BindingFlags.Static | BindingFlags.NonPublic);
        if (field == null)
        {
            return null;
        }

        return field.GetValue(null)?.ToString();
    }

    [UnconditionalSuppressMessage("Trimmer", "IL2075", Justification = "Only used by debugging. If trimming is enabled then missing data is not displayed in debugger.")]
    internal static List<IMethod>? GetServiceMethods(Type clientType)
    {
        // Attempt to get the service methods from generated method fields.
        // If methods can't be resolved then the collection in the client type proxy is null.
        var parentType = GetParentType(clientType);
        if (parentType == null)
        {
            return null;
        }

        var methods = new List<IMethod>();

        var fields = parentType.GetFields(BindingFlags.Static | BindingFlags.NonPublic);
        foreach (var field in fields)
        {
            if (IsMethodField(field))
            {
                methods.Add((IMethod)field.GetValue(null));
            }
        }
        return methods;

        static bool IsMethodField(FieldInfo field) =>
            typeof(IMethod).IsAssignableFrom(field.FieldType);
    }
}
