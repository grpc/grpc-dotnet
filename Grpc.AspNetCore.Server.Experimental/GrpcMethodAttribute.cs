using System;
using Grpc.Core;

namespace Grpc.AspNetCore.Server
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class GrpcMethodAttribute : Attribute
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public GrpcMethodAttribute() { }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name">The name of the method.</param>
        public GrpcMethodAttribute(string name)
        {
            Name = name;
        }

        /// <summary>
        /// The name of the method.
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// The method type.
        /// </summary>
        public MethodType? MethodType { get; set; }

        /// <summary>
        /// The request type for the method.
        /// </summary>
        public Type? RequestType { get; set; }

        /// <summary>
        /// The type to use as the request marshaller.
        /// </summary>
        public Type? RequestMarshallerType { get; set; }

        /// <summary>
        /// The response type for the method.
        /// </summary>
        public Type? ResponseType { get; set; }

        /// <summary>
        /// The type to use as the response marshaller.
        /// </summary>
        public Type? ResponseMarshallerType { get; set; }
    }
}