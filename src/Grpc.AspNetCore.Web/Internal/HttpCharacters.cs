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


namespace Grpc.AspNetCore.Web.Internal
{
    // HttpCharacters is originally from aspnetcore, and has been contributed to grpc-dotnet
    // https://github.com/aspnet/AspNetCore/blob/4dd9bfc492452174f9329c3e1fc4807cb393c05a/src/Servers/Kestrel/Core/src/Internal/Infrastructure/HttpCharacters.cs
    internal static class HttpCharacters
    {
        private static readonly int _tableSize = 128;

        private static readonly bool[] _alphaNumeric = InitializeAlphaNumeric();
        private static readonly bool[] _fieldValue = InitializeFieldValue();
        private static readonly bool[] _token = InitializeToken();

        private static bool[] InitializeAlphaNumeric()
        {
            // ALPHA and DIGIT https://tools.ietf.org/html/rfc5234#appendix-B.1
            var alphaNumeric = new bool[_tableSize];
            for (var c = '0'; c <= '9'; c++)
            {
                alphaNumeric[c] = true;
            }
            for (var c = 'A'; c <= 'Z'; c++)
            {
                alphaNumeric[c] = true;
            }
            for (var c = 'a'; c <= 'z'; c++)
            {
                alphaNumeric[c] = true;
            }
            return alphaNumeric;
        }

        private static bool[] InitializeFieldValue()
        {
            // field-value https://tools.ietf.org/html/rfc7230#section-3.2
            var fieldValue = new bool[_tableSize];
            for (var c = 0x20; c <= 0x7e; c++) // VCHAR and SP
            {
                fieldValue[c] = true;
            }
            return fieldValue;
        }

        private static bool[] InitializeToken()
        {
            // tchar https://tools.ietf.org/html/rfc7230#appendix-B
            var token = new bool[_tableSize];
            Array.Copy(_alphaNumeric, token, _tableSize);
            token['!'] = true;
            token['#'] = true;
            token['$'] = true;
            token['%'] = true;
            token['&'] = true;
            token['\''] = true;
            token['*'] = true;
            token['+'] = true;
            token['-'] = true;
            token['.'] = true;
            token['^'] = true;
            token['_'] = true;
            token['`'] = true;
            token['|'] = true;
            token['~'] = true;
            return token;
        }

        public static int IndexOfInvalidTokenChar(string s)
        {
            var token = _token;

            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c >= (uint)token.Length || !token[c])
                {
                    return i;
                }
            }

            return -1;
        }

        public static int IndexOfInvalidFieldValueChar(string s)
        {
            var fieldValue = _fieldValue;

            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c >= (uint)fieldValue.Length || !fieldValue[c])
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
