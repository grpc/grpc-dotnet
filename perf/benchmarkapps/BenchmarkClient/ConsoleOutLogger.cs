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

using System;
using Grpc.Core.Logging;

namespace BenchmarkClient
{
    public class ConsoleOutLogger : TextWriterLogger
    {
        /// <summary>Creates a console logger not associated to any specific type.</summary>
        public ConsoleOutLogger() : this(null)
        {
        }

        /// <summary>Creates a console logger that logs messsage specific for given type.</summary>
        private ConsoleOutLogger(Type? forType) : base(() => Console.Out, forType)
        {
        }

        /// <summary>
        /// Returns a logger associated with the specified type.
        /// </summary>
        public override ILogger ForType<T>()
        {
            if (typeof(T) == AssociatedType)
            {
                return this;
            }
            return new ConsoleOutLogger(typeof(T));
        }
    }
}