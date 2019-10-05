using System;

namespace Grpc.AspNetCore.Server
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public sealed class GrpcMarshallerAttribute : Attribute
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="type">The name of the method.</param>
        public GrpcMarshallerAttribute(Type type)
        {
            MarshallerType = type;
        }

        /// <summary>
        /// The type of the Marshaller to create for the message.
        /// </summary>
        public Type MarshallerType { get; set; }
    }
}