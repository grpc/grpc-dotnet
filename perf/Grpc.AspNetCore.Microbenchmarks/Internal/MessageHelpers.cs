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

using System.IO;
using System.IO.Pipelines;
using Google.Protobuf;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Tests.Shared;

namespace Grpc.AspNetCore.Microbenchmarks.Internal
{
    internal static class MessageHelpers
    {
        public static void WriteMessage<T>(Stream stream, T message, HttpContextServerCallContext? callContext = null)
            where T : class, IMessage
        {
            var pipeWriter = PipeWriter.Create(stream);

            PipeExtensions.WriteMessageAsync(pipeWriter, message, callContext ?? HttpContextServerCallContextHelper.CreateServerCallContext(), (r, c) => c.Complete(r.ToByteArray()), canFlush: true).GetAwaiter().GetResult();
        }
    }
}
