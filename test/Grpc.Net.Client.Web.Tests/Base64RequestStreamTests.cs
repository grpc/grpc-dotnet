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
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Grpc.Net.Client.Web.Internal;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Web.Tests
{
    [TestFixture]
    public class Base64RequestStreamTests
    {
        [Test]
        public async Task WriteAsync_SmallData_Written()
        {
            // Arrange
            var ms = new MemoryStream();
            var gprcWebStream = new Base64RequestStream(ms);

            var data = Encoding.UTF8.GetBytes("123");

            // Act
            await gprcWebStream.WriteAsync(data);

            // Assert
            var base64 = Encoding.UTF8.GetString(ms.ToArray());
            CollectionAssert.AreEqual(data, Convert.FromBase64String(base64));
        }

        [Test]
        public async Task WriteAsync_VeryLargeData_Written()
        {
            // Arrange
            var ms = new MemoryStream();
            var gprcWebStream = new Base64RequestStream(ms);

            var s = string.Create<object>(16384, null!, (s, o) =>
            {
                for (var i = 0; i < s.Length; i++)
                {
                    s[i] = Convert.ToChar(i % 10);
                }
            });
            var data = Encoding.UTF8.GetBytes(s);

            // Act
            await gprcWebStream.WriteAsync(data).AsTask().DefaultTimeout();
            await gprcWebStream.FlushAsync().DefaultTimeout();

            // Assert
            var base64 = Encoding.UTF8.GetString(ms.ToArray());
            var result = Convert.FromBase64String(base64);
            CollectionAssert.AreEqual(data, result);
        }

        [Test]
        public async Task WriteAsync_MultipleSingleBytes_Written()
        {
            // Arrange
            var ms = new MemoryStream();
            var gprcWebStream = new Base64RequestStream(ms);

            var data = Encoding.UTF8.GetBytes("123");

            // Act
            foreach (var b in data)
            {
                await gprcWebStream.WriteAsync(new[] { b });
            }

            // Assert
            var base64 = Encoding.UTF8.GetString(ms.ToArray());
            CollectionAssert.AreEqual(data, Convert.FromBase64String(base64));

            HttpRequestMessage m = new HttpRequestMessage();
            HttpResponseMessage mm = new HttpResponseMessage();
            mm.TrailingHeaders.Add("test", "value");
        }

        [Test]
        public async Task WriteAsync_SmallDataWithRemainder_WrittenWithoutRemainder()
        {
            // Arrange
            var ms = new MemoryStream();
            var gprcWebStream = new Base64RequestStream(ms);

            var data = Encoding.UTF8.GetBytes("Hello world");

            // Act
            await gprcWebStream.WriteAsync(data);

            // Assert
            var base64 = Encoding.UTF8.GetString(ms.ToArray());
            var newData = Convert.FromBase64String(base64);
            CollectionAssert.AreEqual(data.AsSpan(0, newData.Length).ToArray(), newData);
        }

        [Test]
        public async Task FlushAsync_HasRemainder_WriteRemainder()
        {
            // Arrange
            var ms = new MemoryStream();
            var gprcWebStream = new Base64RequestStream(ms);

            var data = Encoding.UTF8.GetBytes("Hello world");

            // Act
            await gprcWebStream.WriteAsync(data).AsTask().DefaultTimeout();
            await gprcWebStream.FlushAsync().DefaultTimeout();

            // Assert
            var base64 = Encoding.UTF8.GetString(ms.ToArray());
            CollectionAssert.AreEqual(data, Convert.FromBase64String(base64));
        }
    }
}
