namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// Options used to configure methods.
    /// </summary>
    public class GrpcServiceMethodOptions
    {
        internal GrpcServiceMethodOptions(string name)
        {
            Name = name;
        }

        // Fast check for interceptors is used per-request
        internal bool HasInterceptors { get; set; }

        /// <summary>
        /// The name of the method.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Get a collection of interceptors to be executed with every call to the method.
        /// </summary>
        /// <remarks>These interceptors are combined with the global and service level interceptors that have been configured.</remarks>
        public InterceptorCollection Interceptors { get; } = new InterceptorCollection();
    }
}