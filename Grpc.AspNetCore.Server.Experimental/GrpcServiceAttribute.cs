using System;

namespace Grpc.AspNetCore.Server
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GrpcServiceAttribute : Attribute
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public GrpcServiceAttribute() { }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name">The name of the service.</param>
        public GrpcServiceAttribute(string name)
        {
            Name = name;
        }

        /// <summary>
        /// The name of the service.
        /// </summary>
        public string? Name { get; }
    }
}