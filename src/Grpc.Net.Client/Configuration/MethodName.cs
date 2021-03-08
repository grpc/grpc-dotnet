#region Copyright notice and license

// Copyright 2019 The gRPC Authors
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

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Grpc.Net.Client.Configuration
{
    /// <summary>
    /// The name of a method. Used to configure what calls a <see cref="MethodConfig"/> applies to using
    /// the <see cref="MethodConfig.Names"/> collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Represents the <c>Name</c> message in https://github.com/grpc/grpc-proto/blob/master/grpc/service_config/service_config.proto.
    /// </para>
    /// <para>
    /// If a name's <see cref="MethodName.Method"/> property isn't set then the method config is the default
    /// for all methods for the specified service.
    /// </para>
    /// <para>
    /// If a name's <see cref="MethodName.Service"/> property isn't set then <see cref="MethodName.Method"/> must also be unset,
    /// and the method config is the default for all methods on all services.
    /// <see cref="MethodName.Default"/> represents this global default name.
    /// </para>
    /// <para>
    /// When determining which method config to use for a given RPC, the most specific match wins. A method config
    /// with a configured <see cref="MethodName"/> that exactly matches a call's method and service will be used
    /// instead of a service or global default method config.
    /// </para>
    /// </remarks>
    public sealed class MethodName
        : ConfigObject
    {
        /// <summary>
        /// A global default name.
        /// </summary>
        public static readonly MethodName Default = new MethodName(new ReadOnlyDictionary<string, object>(new Dictionary<string, object>()));

        private const string ServicePropertyName = "service";
        private const string MethodPropertyName = "method";

        /// <summary>
        /// Initializes a new instance of the <see cref="MethodName"/> class.
        /// </summary>
        public MethodName() { }
        internal MethodName(IDictionary<string, object> inner) : base(inner) { }

        /// <summary>
        /// Gets or sets the service name.
        /// </summary>
        public string? Service
        {
            get => GetValue<string>(ServicePropertyName);
            set => SetValue(ServicePropertyName, value);
        }

        /// <summary>
        /// Gets or sets the method name.
        /// </summary>
        public string? Method
        {
            get => GetValue<string>(MethodPropertyName);
            set => SetValue(MethodPropertyName, value);
        }
    }
}
