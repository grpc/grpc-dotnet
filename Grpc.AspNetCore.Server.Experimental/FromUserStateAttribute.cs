using System;

namespace Grpc.AspNetCore.Server
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class FromUserStateAttribute : Attribute
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="key">The name of the key that the item exists as.</param>
        public FromUserStateAttribute(string key)
        {
            Key = key;
        }

        /// <summary>
        /// The name of the key that the item exists as.
        /// </summary>
        public string Key { get; }
    }
}