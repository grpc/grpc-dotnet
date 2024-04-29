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

using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Web.Client;

[TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http1)]
[TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http2)]
#if NET7_0_OR_GREATER
[TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http3WithTls)]
#endif
[TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http1)]
[TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http2)]
#if NET7_0_OR_GREATER
[TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http3WithTls)]
#endif
[TestFixture(GrpcTestMode.Grpc, TestServerEndpointName.Http2)]
#if NET7_0_OR_GREATER
[TestFixture(GrpcTestMode.Grpc, TestServerEndpointName.Http3WithTls)]
#endif
public class TrailerMetadataTests : GrpcWebFunctionalTestBase
{
    public TrailerMetadataTests(GrpcTestMode grpcTestMode, TestServerEndpointName endpointName)
     : base(grpcTestMode, endpointName)
    {
    }

    [Test]
    public async Task GetTrailers_LargeTrailer_ReturnedToClient()
    {
        var trailerValue = new string('!', 8000);
        Task<HelloReply> LargeTrailer(HelloRequest request, ServerCallContext context)
        {
            context.ResponseTrailers.Add(new Metadata.Entry("Name", trailerValue));
            return Task.FromResult(new HelloReply());
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(LargeTrailer);

        var channel = CreateGrpcWebChannel();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new HelloRequest());

        await call.ResponseAsync.DefaultTimeout();

        // Assert
        var trailers = call.GetTrailers();
        Assert.AreEqual(1, trailers.Count);
        Assert.AreEqual(trailerValue, trailers.GetValue("name"));
    }

    [Test]
    public async Task GetTrailers_LineBreakAndColon_ReturnedToClient()
    {
        var manyTrailerValues = new Dictionary<string, string>
        {
            ["One"] = new string('1', 1),
            ["Two"] = new string('2', 2),
            ["Three"] = new string('3', 3),
            ["Four"] = new string('4', 4),
            ["Five"] = new string('5', 5),
            ["Six"] = new string('6', 6),
            ["Seven"] = new string('7', 7),
            ["Eight"] = new string('8', 8),
            ["Nine"] = new string('9', 9),
        };
        Task<HelloReply> LargeTrailer(HelloRequest request, ServerCallContext context)
        {
            foreach (var trailer in manyTrailerValues)
            {
                context.ResponseTrailers.Add(new Metadata.Entry(trailer.Key, trailer.Value));
            }
            return Task.FromResult(new HelloReply());
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(LargeTrailer);

        var channel = CreateGrpcWebChannel();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new HelloRequest());

        await call.ResponseAsync.DefaultTimeout();

        // Assert
        var trailers = call.GetTrailers();
        Assert.AreEqual(manyTrailerValues.Count, trailers.Count);
        foreach (var trailer in manyTrailerValues)
        {
            var value = trailers.Single(m => m.Key == trailer.Key.ToLowerInvariant()).Value;
            Assert.AreEqual(trailer.Value, value);
        }
    }
}
