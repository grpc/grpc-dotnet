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
using Grpc.AspNetCore.Server;
using Grpc.Core.Interceptors;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class InterceptorCollectionTests
    {
        [Test]
        public void Add_NonGeneric_AddedToCollection()
        {
            // Arrange
            var interceptors = new InterceptorCollection();

            // Act
            interceptors.Add(typeof(TestInterceptor));

            // Assert
            Assert.AreEqual(1, interceptors.Count);
            Assert.AreEqual(typeof(TestInterceptor), interceptors[0].Type);
            Assert.AreEqual(0, interceptors[0].Arguments.Count);
        }

        [Test]
        public void Add_NonGenericInterceptorBaseType_ThrowError()
        {
            // Arrange
            var interceptors = new InterceptorCollection();

            // Act
            var ex = Assert.Throws<ArgumentException>(() => interceptors.Add(typeof(Interceptor)))!;

            // Assert
            Assert.AreEqual("Type must inherit from Grpc.Core.Interceptors.Interceptor. (Parameter 'interceptorType')", ex.Message);
        }

        [Test]
        public void Add_Generic_AddedToCollection()
        {
            // Arrange
            var interceptors = new InterceptorCollection();

            // Act
            interceptors.Add<TestInterceptor>();

            // Assert
            Assert.AreEqual(1, interceptors.Count);
            Assert.AreEqual(typeof(TestInterceptor), interceptors[0].Type);
            Assert.AreEqual(0, interceptors[0].Arguments.Count);
        }

        [Test]
        public void Add_GenericInterceptorBaseType_AddedToCollection()
        {
            // Arrange
            var interceptors = new InterceptorCollection();

            // Act
            var ex = Assert.Throws<ArgumentException>(() => interceptors.Add<Interceptor>())!;

            // Assert
            Assert.AreEqual("Type must inherit from Grpc.Core.Interceptors.Interceptor. (Parameter 'interceptorType')", ex.Message);
        }

        [Test]
        public void Add_NonGenericWithArgs_AddedToCollection()
        {
            // Arrange
            var interceptors = new InterceptorCollection();

            // Act
            interceptors.Add(typeof(TestInterceptor), "Arg");

            // Assert
            Assert.AreEqual(1, interceptors.Count);
            Assert.AreEqual(typeof(TestInterceptor), interceptors[0].Type);
            Assert.AreEqual(1, interceptors[0].Arguments.Count);
            Assert.AreEqual("Arg", interceptors[0].Arguments[0]);
        }

        [Test]
        public void Add_GenericWithArgs_AddedToCollection()
        {
            // Arrange
            var interceptors = new InterceptorCollection();

            // Act
            interceptors.Add<TestInterceptor>("Arg");

            // Assert
            Assert.AreEqual(1, interceptors.Count);
            Assert.AreEqual(typeof(TestInterceptor), interceptors[0].Type);
            Assert.AreEqual(1, interceptors[0].Arguments.Count);
            Assert.AreEqual("Arg", interceptors[0].Arguments[0]);
        }

        private class TestInterceptor : Interceptor
        {
        }
    }
}
