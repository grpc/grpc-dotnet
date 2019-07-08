﻿#region Copyright notice and license

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

using System.IO;
using System.IO.Pipelines;
using Google.Protobuf;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;

namespace Grpc.AspNetCore.Microbenchmarks.Internal
{
    internal static class MessageHelpers
    {
        private static readonly HttpContextServerCallContext TestServerCallContext;

        static MessageHelpers()
        {
            TestServerCallContext = new HttpContextServerCallContext(SystemClock.Instance, TestObjectPool.Instance);
            TestServerCallContext.Initialize(new DefaultHttpContext(), new GrpcServiceOptions(), NullLogger.Instance);
        }

        public static void WriteMessage<T>(Stream stream, T message) where T : IMessage
        {
            var messageData = message.ToByteArray();

            var pipeWriter = PipeWriter.Create(stream);

            PipeExtensions.WriteMessageAsync(pipeWriter, messageData, TestServerCallContext, flush: true).GetAwaiter().GetResult();
        }
    }

    internal class TestObjectPool : ObjectPool<HttpContextServerCallContext>
    {
        public static TestObjectPool Instance = new TestObjectPool();

        private HttpContextServerCallContext? _obj;

        public override HttpContextServerCallContext Get()
        {
            return _obj ??= new HttpContextServerCallContext(SystemClock.Instance, TestObjectPool.Instance);
        }

        public override void Return(HttpContextServerCallContext obj)
        {
            obj.Reset();
            _obj = obj;
        }
    }
}
