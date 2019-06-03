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
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Model.Internal;
using Grpc.AspNetCore.Server.Tests.TestObjects.Services.WithAttribute;
using Grpc.AspNetCore.Server.Tests.TestObjects.Services.WithoutAttribute;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class BindMethodFinderTests
    {
        [TestCase(typeof(GreeterWithAttributeService))]
        [TestCase(typeof(GreeterWithAttributeServiceSubClass))]
        [TestCase(typeof(GreeterWithAttributeServiceSubSubClass))]
        [TestCase(typeof(GreeterWithoutAttributeService))]
        [TestCase(typeof(GreeterWithoutAttributeServiceSubClass))]
        [TestCase(typeof(GreeterWithoutAttributeServiceSubSubClass))]
        public void GetBindMethodFallback(Type serviceType)
        {
            var methodInfo = BindMethodFinder.GetBindMethodFallback(serviceType);
            Assert.IsNotNull(methodInfo);
        }

        [TestCase(typeof(GreeterWithAttributeService), true)]
        [TestCase(typeof(GreeterWithAttributeServiceSubClass), true)]
        [TestCase(typeof(GreeterWithAttributeServiceSubSubClass), true)]
        [TestCase(typeof(GreeterWithoutAttributeService), false)]
        [TestCase(typeof(GreeterWithoutAttributeServiceSubClass), false)]
        [TestCase(typeof(GreeterWithoutAttributeServiceSubSubClass), false)]
        public void GetBindMethodUsingAttribute(Type serviceType, bool foundMethod)
        {
            var methodInfo = BindMethodFinder.GetBindMethodUsingAttribute(serviceType);
            Assert.AreEqual(foundMethod, methodInfo != null);
        }
    }
}
