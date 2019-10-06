using System;
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Model.Internal
{
    internal static class TypeExtensions
    {
        internal static bool IsServerStreamWriter(this Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IServerStreamWriter<>);
        }
    }
}