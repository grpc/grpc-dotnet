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
using Grpc.Net.Client.Configuration;

namespace Grpc.Net.Client.Internal.Configuration
{
    internal struct ConfigProperty<TValue, TInner> where TValue : IConfigValue
    {
        private TValue? _value;
        private readonly Func<TInner?, TValue?> _valueFactory;
        private readonly string _key;

        public ConfigProperty(Func<TInner?, TValue?> valueFactory, string key)
        {
            _value = default;
            _valueFactory = valueFactory;
            _key = key;
        }

        public TValue? GetValue(ConfigObject inner)
        {
            if (_value == null)
            {
                var innerValue = inner.GetValue<TInner>(_key);
                _value = _valueFactory(innerValue);

                if (_value != null && innerValue == null)
                {
                    // Set newly created value
                    SetValue(inner, _value);
                }
            }

            return _value;
        }

        public void SetValue(ConfigObject inner, TValue? value)
        {
            _value = value;
            inner.SetValue(_key, _value?.Inner);
        }
    }
}
