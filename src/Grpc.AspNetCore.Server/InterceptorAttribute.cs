using System;

namespace Grpc.AspNetCore.Server
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InterceptorAttribute : Attribute
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="interceptorType">The interceptor type.</param>
        /// <param name="args">An array of arguments that match in number, order, and type the parameters of the constructor to invoke.</param>
        public InterceptorAttribute(Type interceptorType, params object[] args)
        {
            InterceptorType = interceptorType;
            Args = args;
        }

        /// <summary>
        /// The interceptor type.
        /// </summary>
        public Type InterceptorType { get; }

        /// <summary>
        /// An array of arguments that match in number, order, and type the parameters of the constructor to invoke.
        /// </summary>
        public object[] Args { get; }
    }
}