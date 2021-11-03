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
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Retry;

namespace Client
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            using var channel = CreateChannel();
            var client = new Retrier.RetrierClient(channel);

            await UnaryRetry(client);

            Console.WriteLine("Shutting down");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task UnaryRetry(Retrier.RetrierClient client)
        {
            Console.WriteLine("Delivering packages...");
            foreach (var product in Products)
            {
                try
                {
                    var package = new Package { Name = product };
                    var call = client.DeliverPackageAsync(package);
                    var response = await call;

                    #region Print success
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(response.Message);
                    Console.ResetColor();
                    Console.Write(" " + await GetRetryCount(call.ResponseHeadersAsync));
                    Console.WriteLine();
                    #endregion
                }
                catch (RpcException ex)
                {
                    #region Print failure
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(ex.Status.Detail);
                    Console.ResetColor();
                    #endregion
                }

                await Task.Delay(TimeSpan.FromSeconds(0.2));
            }
        }

        private static GrpcChannel CreateChannel()
        {
            var methodConfig = new MethodConfig
            {
                Names = { MethodName.Default },
                RetryPolicy = new RetryPolicy
                {
                    MaxAttempts = 5,
                    InitialBackoff = TimeSpan.FromSeconds(0.5),
                    MaxBackoff = TimeSpan.FromSeconds(0.5),
                    BackoffMultiplier = 1,
                    RetryableStatusCodes = { StatusCode.Unavailable }
                }
            };

            return GrpcChannel.ForAddress("https://localhost:5001", new GrpcChannelOptions
            {
                ServiceConfig = new ServiceConfig { MethodConfigs = { methodConfig } }
            });
        }

        private static async Task<string> GetRetryCount(Task<Metadata> responseHeadersTask)
        {
            var headers = await responseHeadersTask;
            var previousAttemptCount = headers.GetValue("grpc-previous-rpc-attempts");
            return previousAttemptCount != null ? $"(retry count: {previousAttemptCount})" : string.Empty;
        }

        private static readonly IList<string> Products = new List<string>
        {
            "Secrets of Silicon Valley",
            "The Busy Executive's Database Guide",
            "Emotional Security: A New Algorithm",
            "Prolonged Data Deprivation: Four Case Studies",
            "Cooking with Computers: Surreptitious Balance Sheets",
            "Silicon Valley Gastronomic Treats",
            "Sushi, Anyone?",
            "Fifty Years in Buckingham Palace Kitchens",
            "But Is It User Friendly?",
            "You Can Combat Computer Stress!",
            "Is Anger the Enemy?",
            "Life Without Fear",
            "The Gourmet Microwave",
            "Onions, Leeks, and Garlic: Cooking Secrets of the Mediterranean",
            "The Psychology of Computer Cooking",
            "Straight Talk About Computers",
            "Computer Phobic AND Non-Phobic Individuals: Behavior Variations",
            "Net Etiquette"
        };
    }
}
