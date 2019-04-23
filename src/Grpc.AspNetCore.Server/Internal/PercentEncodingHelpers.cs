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
using System.Runtime.CompilerServices;
using System.Text;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class PercentEncodingHelpers
    {
        private static readonly char[] HexChars = new[]
        {
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F'
        };
        internal const int MaxUnicodeCharsReallocate = 40; // Maximum batch size when working with unicode characters
        private const int MaxUtf8BytesPerUnicodeChar = 4;
        private const int AsciiMaxValue = 127;

        // From https://github.com/grpc/grpc/blob/324189c9dc540f0693d79f02dcb8c5f9261b535e/src/core/lib/slice/percent_encoding.cc#L31
        private static readonly byte[] PercentEncodingUnreservedBitField =
        {
            0x00, 0x00, 0x00, 0x00, 0xdf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
            0xff, 0xff, 0xff, 0xff, 0x7f, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        public static string PercentEncode(string value)
        {
            // Count the number of bytes needed to output this string
            var encodedLength = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c > AsciiMaxValue)
                {
                    // Get additional unicode characters
                    var unicodeCharCount = GetUnicodeCharCount(value, i);

                    var unicodeCharacters = Encoding.UTF8.GetByteCount(value.AsSpan(i, unicodeCharCount));
                    encodedLength += unicodeCharacters * 3;
                    i += unicodeCharCount - 1;
                }
                else
                {
                    bool unres = IsUnreservedCharacter(c);
                    encodedLength += unres ? 1 : 3;
                }
            }

            // Return the string if no encoding is required
            if (value.Length == encodedLength)
            {
                return value;
            }

            // Encode
            return string.Create(encodedLength, value, Encode);

            static void Encode(Span<char> span, string s)
            {
                Span<byte> unicodeBytesBuffer = stackalloc byte[MaxUnicodeCharsReallocate * MaxUtf8BytesPerUnicodeChar];

                var writePosition = 0;
                for (var i = 0; i < s.Length; i++)
                {
                    var current = s[i];
                    if (current > AsciiMaxValue)
                    {
                        // Get additional unicode characters
                        var unicodeCharCount = GetUnicodeCharCount(s, i);

                        var numberOfBytes = Encoding.UTF8.GetBytes(s.AsSpan(i, unicodeCharCount), unicodeBytesBuffer);

                        for (var count = 0; count < numberOfBytes; count++)
                        {
                            EscapeAsciiChar(ref span, ref writePosition, (char)unicodeBytesBuffer[count]);
                        }
                        i += unicodeCharCount - 1;
                    }
                    else if (IsUnreservedCharacter(current))
                    {
                        span[writePosition++] = current;
                    }
                    else
                    {
                        EscapeAsciiChar(ref span, ref writePosition, current);
                    }
                }
            }
        }

        private static void EscapeAsciiChar(ref Span<char> span, ref int writePosition, char current)
        {
            span[writePosition++] = '%';
            span[writePosition++] = HexChars[current >> 4];
            span[writePosition++] = HexChars[current & 15];
        }

        private static int GetUnicodeCharCount(string value, int currentIndex)
        {
            var unicodeCharCount = 0;
            var maxSize = Math.Min(value.Length - currentIndex, MaxUnicodeCharsReallocate - 1); // Leave a character for possible low surrogate
            for (; unicodeCharCount < maxSize && value[currentIndex + unicodeCharCount] > AsciiMaxValue; unicodeCharCount++)
            {
            }

            if (char.IsHighSurrogate(value[currentIndex + unicodeCharCount - 1]))
            {
                if (unicodeCharCount < value.Length - currentIndex && char.IsLowSurrogate(value[currentIndex + unicodeCharCount]))
                {
                    // Last character is a high surrogate so check ahead to see if it is followed by a low surrogate and include
                    unicodeCharCount++;
                }
            }

            return unicodeCharCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsUnreservedCharacter(char c)
        {
            return ((PercentEncodingUnreservedBitField[c / 8] >> (c % 8)) & 1) != 0;
        }

        // TODO(JamesNK): Remove WIP decode once we are certain Uri.UnescapeData works correctly
#if Decode
        public static string PercentDecode(string value)
        {
            var writeLength = 0;
            var readIndex = 0;
            while (readIndex < value.Length)
            {
                if (value[readIndex] == '%')
                {
                    if (readIndex + 2 < value.Length && IsValidHex(value[readIndex + 1]) && IsValidHex(value[readIndex + 2]))
                    {
                        readIndex += 3;
                        writeLength++;
                    }
                    else
                    {
                        readIndex++;
                        writeLength++;
                    }
                }
                else
                {
                    readIndex++;
                    writeLength++;
                }
            }

            if (writeLength == value.Length)
            {
                return value;
            }

            return string.Create(writeLength, value, Decode);

            static void Decode(Span<char> span, string s)
            {
                var readIndex = 0;
                var writeIndex = 0;
                while (readIndex < s.Length)
                {
                    if (s[readIndex] == '%')
                    {
                        if (readIndex + 2 < s.Length && IsValidHex(s[readIndex + 1]) && IsValidHex(s[readIndex + 1]))
                        {
                            span[writeIndex++] = (char)((DecodeHex(s[readIndex + 1]) << 4) | DecodeHex(s[readIndex + 2]));
                            readIndex += 3;
                        }
                        else
                        {
                            span[writeIndex++] = s[readIndex++];
                        }
                    }
                    else
                    {
                        span[writeIndex++] = s[readIndex++];
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsValidHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char DecodeHex(char c)
        {
            if (c >= '0' && c <= '9') return (char)(c - '0');
            if (c >= 'A' && c <= 'F') return (char)(c - 'A' + 10);
            if (c >= 'a' && c <= 'f') return (char)(c - 'a' + 10);
            return (char)255;
        }
#endif
    }
}