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

using Microsoft.Extensions.Logging;

namespace GRPCServer
{
    public class TicketRepository
    {
        private readonly ILogger<TicketRepository> _logger;
        private int _availableTickets = 5;
        
        public TicketRepository(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<TicketRepository>();
        }

        public int GetAvailableTickets()
        {
            return _availableTickets;
        }

        public bool BuyTickets(string user, int count)
        {
            var updatedCount = _availableTickets - count;

            // Negative ticket count means there weren't enough available tickets
            if (updatedCount < 0)
            {
                _logger.LogError($"{user} failed to purchase tickets. Not enough available tickets.");
                return false;
            }

            _availableTickets = updatedCount;

            _logger.LogError($"{user} successfully purchased tickets.");
            return true;
        }
    }
}
