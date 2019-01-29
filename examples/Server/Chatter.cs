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
using Grpc.Core;
using Chat;
using Microsoft.Extensions.Logging;

namespace GRPCServer
{
    public class ChatterService : Chatter.ChatterBase
    {
        private readonly ILogger _logger;

        public ChatterService(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ChatterService>();
        }

        private static HashSet<IServerStreamWriter<ChatMessage>> _subscribers = new HashSet<IServerStreamWriter<ChatMessage>>();

        public override async Task Chat(IAsyncStreamReader<ChatMessage> requestStream, IServerStreamWriter<ChatMessage> responseStream, ServerCallContext context)
        {
            if (!await requestStream.MoveNext())
            {
                // No messages so don't register and just exit.
                return;
            }

            var user = requestStream.Current.Name;

            // Warning, the following is very racy but good enough for a proof of concept
            // Register subscriber
            _logger.LogInformation($"{user} connected");
            _subscribers.Add(responseStream);

            do
            {
                await BroadcastMessageAsync(requestStream.Current, _logger);
            } while (await requestStream.MoveNext());

            _subscribers.Remove(responseStream);
            _logger.LogInformation($"{user} disconnected");
        }

        private static async Task BroadcastMessageAsync(ChatMessage message, ILogger logger)
        {
            foreach (var subscriber in _subscribers)
            {
                logger.LogInformation($"Broadcasting: {message.Name} - {message.Message}");
                await subscriber.WriteAsync(message);
            }
        }
    }
}
