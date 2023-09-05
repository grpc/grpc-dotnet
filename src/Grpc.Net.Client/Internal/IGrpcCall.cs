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

using System.Diagnostics.CodeAnalysis;
using Grpc.Core;

namespace Grpc.Net.Client.Internal;

internal interface IGrpcCall<TRequest, TResponse> : IDisposable, IEnumerable<KeyValuePair<string, object>>
    where TRequest : class
    where TResponse : class
{
    Task<TResponse> GetResponseAsync();
    Task<Metadata> GetResponseHeadersAsync();
    Status GetStatus();
    Metadata GetTrailers();

    IClientStreamWriter<TRequest>? ClientStreamWriter { get; }
    IAsyncStreamReader<TResponse>? ClientStreamReader { get; }

    void StartUnary(TRequest request);
    void StartClientStreaming();
    void StartServerStreaming(TRequest request);
    void StartDuplexStreaming();

    Task WriteClientStreamAsync<TState>(
        Func<GrpcCall<TRequest, TResponse>, Stream, CallOptions, TState, Task> writeFunc,
        TState state,
        CancellationToken cancellationToken);

    Exception CreateFailureStatusException(Status status);

    bool TryRegisterCancellation(
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out CancellationTokenRegistration? cancellationTokenRegistration);

    object? CallWrapper { get; set; }
    bool Disposed { get; }
    bool ResponseFinished { get; }
    int MessagesRead { get; }
}
