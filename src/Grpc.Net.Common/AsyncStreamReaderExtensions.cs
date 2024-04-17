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

using System.Runtime.CompilerServices;
using Grpc.Shared;

namespace Grpc.Core;

/// <summary>
/// Extension methods for <see cref="IAsyncStreamReader{T}"/>.
/// </summary>
public static class AsyncStreamReaderExtensions
{
    /// <summary>
    /// Creates an <see cref="IAsyncEnumerable{T}"/> that enables reading all of the data from the stream reader.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="streamReader">The stream reader.</param>
    /// <param name="cancellationToken">The cancellation token to use to cancel the enumeration.</param>
    /// <returns>The created async enumerable.</returns>
    public static IAsyncEnumerable<T> ReadAllAsync<T>(this IAsyncStreamReader<T> streamReader, CancellationToken cancellationToken = default)
    {
        ArgumentNullThrowHelper.ThrowIfNull(streamReader);

        return ReadAllAsyncCore(streamReader, cancellationToken);
    }

    private static async IAsyncEnumerable<T> ReadAllAsyncCore<T>(IAsyncStreamReader<T> streamReader, [EnumeratorCancellation]CancellationToken cancellationToken)
    {
        while (await streamReader.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            yield return streamReader.Current;
        }
    }
}
