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

using System.Collections.Generic;
using System.Threading.Tasks;
using Chat;
using Grpc.Core;

namespace FunctionalTestsWebsite.Services
{
    public class ChatterService : Chatter.ChatterBase
    {
        private static HashSet<IServerStreamWriter<ChatMessage>> _subscribers = new HashSet<IServerStreamWriter<ChatMessage>>();

        public override Task Chat(IAsyncStreamReader<ChatMessage> requestStream, IServerStreamWriter<ChatMessage> responseStream, ServerCallContext context)
        {
            return ChatCore(requestStream, responseStream);
        }

        public static async Task ChatCore(IAsyncStreamReader<ChatMessage> requestStream, IServerStreamWriter<ChatMessage> responseStream)
        {
            if (!await requestStream.MoveNext())
            {
                // No messages so don't register and just exit.
                return;
            }

            // Warning, the following is very racy
            _subscribers.Add(responseStream);

            do
            {
                await BroadcastMessageAsync(requestStream.Current);
            } while (await requestStream.MoveNext());

            _subscribers.Remove(responseStream);
        }

        private static async Task BroadcastMessageAsync(ChatMessage message)
        {
            foreach (var subscriber in _subscribers)
            {
                await subscriber.WriteAsync(message);
            }
        }
    }
}
