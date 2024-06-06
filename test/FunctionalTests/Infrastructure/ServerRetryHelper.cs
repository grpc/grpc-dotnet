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

using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure;

// Copied with permission from https://github.com/dotnet/aspnetcore/blob/3612a9f261c696447844eb748a545bd062beeab4/src/Servers/Kestrel/shared/test/ServerRetryHelper.cs
public static class ServerRetryHelper
{
    private const int RetryCount = 20;

    /// <summary>
    /// Retry a func. Useful when a test needs an explicit port and you want to avoid port conflicts.
    /// </summary>
    public static void BindPortsWithRetry(Action<int> retryFunc, ILogger logger)
    {
        var retryCount = 0;

        // Add a random number to starting port to reduce chance of conflicts because of multiple tests using this retry.
        var nextPortAttempt = 30000 + Random.Shared.Next(10000);

        while (true)
        {
            // Find a port that's available for TCP and UDP. Start with the given port search upwards from there.
            var port = GetAvailablePort(nextPortAttempt, logger);

            try
            {
                retryFunc(port);
                break;
            }
            catch (Exception ex)
            {
                retryCount++;
                nextPortAttempt = port + Random.Shared.Next(100);

                if (retryCount >= RetryCount)
                {
                    throw;
                }
                else
                {
                    logger.LogError(ex, "Error running test {RetryCount}. Retrying.", retryCount);
                }
            }
        }
    }

    private static int GetAvailablePort(int startingPort, ILogger logger)
    {
        logger.LogInformation("Searching for free port starting at {startingPort}.", startingPort);

        var unavailableEndpoints = new List<IPEndPoint>();

        var properties = IPGlobalProperties.GetIPGlobalProperties();

        // Ignore active connections
        AddEndpoints(startingPort, unavailableEndpoints, properties.GetActiveTcpConnections().Select(c => c.LocalEndPoint));

        // Ignore active tcp listners
        AddEndpoints(startingPort, unavailableEndpoints, properties.GetActiveTcpListeners());

        // Ignore active UDP listeners
        AddEndpoints(startingPort, unavailableEndpoints, properties.GetActiveUdpListeners());

        logger.LogInformation("Found {count} unavailable endpoints.", unavailableEndpoints.Count);

        for (var i = startingPort; i < ushort.MaxValue; i++)
        {
            var match = unavailableEndpoints.FirstOrDefault(ep => ep.Port == i);
            if (match == null)
            {
                logger.LogInformation("Port {i} free.", i);
                return i;
            }
            else
            {
                logger.LogInformation("Port {i} in use. End point: {match}", i, match);
            }
        }

        throw new Exception($"Couldn't find a free port after {startingPort}.");

        static void AddEndpoints(int startingPort, List<IPEndPoint> endpoints, IEnumerable<IPEndPoint> activeEndpoints)
        {
            foreach (IPEndPoint endpoint in activeEndpoints)
            {
                if (endpoint.Port >= startingPort)
                {
                    endpoints.Add(endpoint);
                }
            }
        }
    }
}
