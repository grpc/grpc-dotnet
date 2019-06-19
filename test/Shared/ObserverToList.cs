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
using System.Collections.Generic;

namespace Grpc.Tests.Shared
{
    internal class ObserverToList<T> : IObserver<T>
    {
        public ObserverToList(List<T> output, Predicate<T>? filter = null, string? name = null)
        {
            _output = output;
            _output.Clear();
            _filter = filter;
            _name = name;
        }

        public bool Completed { get; private set; }

        #region private
        public void OnCompleted()
        {
            Completed = true;
        }

        public void OnError(Exception error)
        {
            throw new Exception("Error happened on IObserver", error);
        }

        public void OnNext(T value)
        {
            if (Completed)
            {
                throw new Exception("Observer completed.");
            }

            if (_filter == null || _filter(value))
            {
                _output.Add(value);
            }
        }

        private List<T> _output;
        private Predicate<T>? _filter;
        private string? _name;  // for debugging 
        #endregion
    }
}
