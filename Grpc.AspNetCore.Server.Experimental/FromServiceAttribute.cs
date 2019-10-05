using System;

namespace Grpc.AspNetCore.Server
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class FromServiceAttribute : Attribute { }
}