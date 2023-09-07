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

using System.Diagnostics;
using System.Net;
using Google.Protobuf;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Shared;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class DiagnosticsTests
{
    [Test]
    public async Task Dispose_StartCallInTask_ActivityPreserved()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: StatusCode.Aborted);
            response.TrailingHeaders().Add(GrpcProtocolConstants.MessageTrailer, "value");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act & Assert
        var a = new Activity("a").Start();
        Assert.AreEqual("a", Activity.Current!.OperationName);

        var call = await Task.Run(() =>
        {
            var c = invoker.AsyncDuplexStreamingCall();
            Assert.AreEqual("a", Activity.Current.OperationName);

            return c;
        });
        Assert.AreEqual("a", Activity.Current.OperationName);

        var b = new Activity("b").Start();
        Assert.AreEqual("b", Activity.Current.OperationName);

        call.Dispose();
        Assert.AreEqual("b", Activity.Current.OperationName);
    }

    [Test]
    public void DiagnosticListener_MakeCall_ActivityWritten()
    {
        // Arrange
        HttpRequestMessage? requestMessage = null;
        HttpResponseMessage? responseMessage = null;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            requestMessage = request;

            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            responseMessage = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: StatusCode.Aborted);
            responseMessage.TrailingHeaders().Add(GrpcProtocolConstants.MessageTrailer, "value");
            return responseMessage;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        var result = new List<KeyValuePair<string, object?>>();

        var dataMessageMarshaller = new Marshaller<DataMessage>(m => m.ToByteArray(), data => DataMessage.Parser.ParseFrom(data));
        var dataMessageMethod = ClientTestHelpers.GetServiceMethod<DataMessage, DataMessage>(
            MethodType.DuplexStreaming,
            dataMessageMarshaller,
            dataMessageMarshaller);

        // Act
        HttpRequestMessage? requestMessage1 = null;
        HttpResponseMessage? responseMessage1 = null;
        HttpRequestMessage? requestMessage2 = null;
        HttpResponseMessage? responseMessage2 = null;

        using (GrpcDiagnostics.DiagnosticListener.Subscribe(new ObserverToList<KeyValuePair<string, object?>>(result)))
        {
            var c1 = invoker.AsyncDuplexStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.DuplexStreaming), string.Empty, new CallOptions());
            c1.Dispose();

            requestMessage1 = requestMessage;
            responseMessage1 = responseMessage;

            var c2 = invoker.AsyncDuplexStreamingCall<DataMessage, DataMessage>(dataMessageMethod, string.Empty, new CallOptions());
            c2.Dispose();

            requestMessage2 = requestMessage;
            responseMessage2 = responseMessage;
        }

        // Assert
        Assert.AreEqual(4, result.Count);

        // First call
        Assert.AreEqual(GrpcDiagnostics.ActivityStartKey, result[0].Key);
        Assert.AreEqual(requestMessage1, GetValueFromAnonymousType<HttpRequestMessage>(result[0].Value!, "Request"));
        Assert.AreEqual(GrpcDiagnostics.ActivityStopKey, result[1].Key);
        Assert.AreEqual(requestMessage1, GetValueFromAnonymousType<HttpRequestMessage>(result[1].Value!, "Request"));
        Assert.AreEqual(responseMessage1, GetValueFromAnonymousType<HttpResponseMessage>(result[1].Value!, "Response"));

        // Second call
        Assert.AreEqual(GrpcDiagnostics.ActivityStartKey, result[2].Key);
        Assert.AreEqual(requestMessage2, GetValueFromAnonymousType<HttpRequestMessage>(result[2].Value!, "Request"));
        Assert.AreEqual(GrpcDiagnostics.ActivityStopKey, result[3].Key);
        Assert.AreEqual(requestMessage2, GetValueFromAnonymousType<HttpRequestMessage>(result[3].Value!, "Request"));
        Assert.AreEqual(responseMessage2, GetValueFromAnonymousType<HttpResponseMessage>(result[3].Value!, "Response"));

        // Check types are expected
        Assert.AreEqual(typeof(GrpcCall.ActivityStartData), result[0].Value!.GetType());
        Assert.AreEqual(typeof(GrpcCall.ActivityStopData), result[1].Value!.GetType());
        Assert.AreEqual(result[0].Value!.GetType(), result[2].Value!.GetType());
        Assert.AreEqual(result[1].Value!.GetType(), result[3].Value!.GetType());

        // Check values are unique for each call
        Assert.AreNotEqual(result[0].Value, result[2].Value);
        Assert.AreNotEqual(result[1].Value, result[3].Value);
    }

    [Test]
    public void DiagnosticListener_MakeCall_ActivityHasNameAndDuration()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var responseMessage = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: StatusCode.Aborted);
            responseMessage.TrailingHeaders().Add(GrpcProtocolConstants.MessageTrailer, "value");
            return responseMessage;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        string? activityName = null;
        TimeSpan? activityDurationOnStop = null;
        Action<KeyValuePair<string, object?>> onDiagnosticMessage = (m) =>
        {
            if (m.Key == GrpcDiagnostics.ActivityStopKey)
            {
                activityName = Activity.Current!.OperationName;
                activityDurationOnStop = Activity.Current.Duration;
            }
        };

        // Act
        using (GrpcDiagnostics.DiagnosticListener.Subscribe(new ActionObserver<KeyValuePair<string, object?>>(onDiagnosticMessage)))
        {
            var c = invoker.AsyncDuplexStreamingCall();
            c.Dispose();
        }

        // Assert
        Assert.AreEqual(GrpcDiagnostics.ActivityName, activityName);
        Assert.IsNotNull(activityDurationOnStop);
        Assert.AreNotEqual(TimeSpan.Zero, activityDurationOnStop);
    }

    [Test]
    public void ActivitySource_MakeCall_ActivityHasNameAndDuration()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var responseMessage = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: StatusCode.Aborted);
            responseMessage.TrailingHeaders().Add(GrpcProtocolConstants.MessageTrailer, "value");
            return responseMessage;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        string? activityName = null;
        TimeSpan? activityDurationOnStop = null;

        // Act
        using (var activityListener = new ActivityListener())
        {
            activityListener.ShouldListenTo = activitySource => activitySource == GrpcDiagnostics.ActivitySource;
            activityListener.Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData;
            activityListener.ActivityStarted = activity =>
            {
                activityName = activity.OperationName;
            };
            activityListener.ActivityStopped = activity =>
            {
                activityDurationOnStop = activity.Duration;
            };

            ActivitySource.AddActivityListener(activityListener);

            var c = invoker.AsyncDuplexStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.DuplexStreaming), string.Empty, new CallOptions());
            c.Dispose();
        }

        // Assert
        Assert.AreEqual(GrpcDiagnostics.ActivityName, activityName);
        Assert.IsNotNull(activityDurationOnStop);
        Assert.AreNotEqual(TimeSpan.Zero, activityDurationOnStop);
    }

    private static T GetValueFromAnonymousType<T>(object dataitem, string itemkey)
    {
        var type = dataitem.GetType();
        T itemvalue = (T)type.GetProperty(itemkey)!.GetValue(dataitem, null)!;
        return itemvalue;
    }

    internal class ActionObserver<T> : IObserver<T>
    {
        private readonly Action<T> _action;

        public ActionObserver(Action<T> action)
        {
            _action = action;
        }

        public bool Completed { get; private set; }

        public void OnCompleted()
        {
            Completed = true;
        }

        public void OnError(Exception error)
        {
            throw new Exception("Observer error", error);
        }

        public void OnNext(T value)
        {
            _action(value);
        }
    }

    internal class ObserverToList<T> : IObserver<T>
    {
        public ObserverToList(List<T> output, Predicate<T>? filter = null, string? name = null)
        {
            _output = output;
            _output.Clear();
            _filter = filter;
            _name = name;
        }

        public bool Completed { get; private set; }

        #region private
        public void OnCompleted()
        {
            Completed = true;
        }

        public void OnError(Exception error)
        {
            Assert.True(false, "Error happened on IObserver");
        }

        public void OnNext(T value)
        {
            Assert.False(Completed);
            if (_filter == null || _filter(value))
            {
                _output.Add(value);
            }
        }

        private readonly List<T> _output;
        private readonly Predicate<T>? _filter;
        private readonly string? _name;  // for debugging
        #endregion
    }
}
