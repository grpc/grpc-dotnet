# Grpc C# API for error handling with Status.proto

This is a protoype NuGet package providing client and server side support for the
[gRPC richer error model](https://grpc.io/docs/guides/error/#richer-error-model).

It had dependencies NuGet packages on:
* `Google.Api.CommonProtos` - to provide the proto implementations used by the richer error model
* `Grpc.Core.Api` - for API classes such as `RpcException`

## Error handling in gRPC

The standard way for gRPC to report the success or failure of a gRPC call is for a
status code to be returned. If a call completes successfully the server returns an `OK`
status to the client, otherwise an error status code is returned. This is known as the
_standard error model_ and is the official gRPC error model supported by all gRPC
implementations.

There is another error model known as the _richer error model_ that allows additional
error details to be included by the server. These are expressed in protocol buffers
messages, and a
[set of standard error message types](https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto)
is defined to cover most needs. The protobuf binary encoding of this extra error
information is provided as trailing metadata in the response.

Not all languages currently have support for this richer error model. It is already
supported in the C++, Go, Java, Python, and Ruby libraries. This NuGet package adds
support for C# and .NET.

For more information on the richer error model see the
[gRPC documentation on error handling](https://grpc.io/docs/guides/error/),
and the [Google APIs overview of the error model](https://cloud.google.com/apis/design/errors#error_model).

## .NET implementation of the richer error model

The error model is define by the protocol buffers files [status.proto](https://github.com/googleapis/googleapis/blob/master/google/rpc/status.proto)
and [error_details.proto](https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto)
and their implementations in classes provided in the `Google.Api.CommonProtos` NuGet package.

The error is encapsulated by an instance of `Google.Rpc.Status` and
returned in the trailing response metadata. Setting and reading this metadata is handled
for you when using the extension methods provided in this package.

## Server Side

The server side uses C#'s Object and Collection initializer syntax.

There are two ways that the server can return additional error information:
- by throwing an `RpcException` that contains the details of the error.
- by setting the `Status` and `ResponseTrailers` in the `ServerCallContext`
before returning from the call. See the examples below.

There are examples of both methods below.  In a .NET client the result will always be
an exception received by the client no matter which of the above methods the server
implements.

The `Google.Rpc.Status` can be created and initialized using C#'s Object and Collection initializer syntax. To add messages to the `Details` repeated field, wrap each one in `Any.Pack()` - see example below.

The `Google.Rpc.Status` extension method `ToRpcException` creates the appropriate `RpcException` from the status.

The `Grpc.Core.Metadata` extension method `SetRpcStatus` adds a binary representation of the status to the metadata.


__Example__ - throwing a `RpcException`:
```C#
throw new Google.Rpc.Status
{
    Code = Google.Rpc.Code.NotFound;
    Message = "Simple error message",
    Details =
    {
        Any.Pack(new ErrorInfo
        {
            Domain = "error example",
            Reason = "some reason"
        }),
        Any.Pack(new RequestInfo
        {
            RequestId = "EchoRequest",
            ServingData = "Param: " + request.Action.ToString()
        }),
        }
}.ToRpcException();
```
__Example__ - setting the status and response trailers instead of throwing an exception:
```C#
context.Status = new Grpc.Core.Status(StatusCode.Internal, "Some detail");
context.ResponseTrailers.SetRpcStatus(new Google.Rpc.Status
{
    Code = Google.Rpc.Code.NotFound,
    Message = "Simple error message",
    Details =
    {
        Any.Pack(new ErrorInfo
        {
            Domain = "error example",
            Reason = "some reason"
        })
    }
});

return Task.FromResult( /* ... */ );
```

### A note on error codes

Both `Grpc.Core.StatusCode` and `Google.Rpc.Code` define enums for a common
set of status codes such as `NotFound`, `PermissionDenied`, etc. They have the same values and are based on the codes defined
in [grpc/status.h](https://github.com/grpc/grpc/blob/master/include/grpc/status.h).

The recommendation is to use the values in `Google.Rpc.Code` as a convention.
This is a must for Google APIs and strongly recommended for third party services.
But users can use a different domain of values if they want and and as long as their
services are mutually compatible, things will work fine.

In the richer error model the `RpcException` will contain both a `Grpc.Core.Status` (for the
standard error mode) and a `Google.Rpc.Status` (for the richer error model), each with their
own status code. While an application is free to set these to different values we recommend
that they are set to the same value to avoid ambiguity.

## Client Side

There is an extension method to retrieve a `Google.Rpc.Status` from the metadata in
an `RpcException`.

Once the `Google.Rpc.Status` has been retrieved the messages in the `Details`
can be unpacked. There are two ways of doing this:

- calling `GetStatusDetails<T>()` with one of the expected message types
- iterating over all the messages in the `Details` using `UnpackDetailMessage()`

__Example__ - calling `GetStatusDetails<T>()`:

```C#
void PrintError(RpcException ex)
{
    // Get the status from the RpcException
    Google.Rpc.Status? rpcStatus = ex.GetRpcStatus(); // Extension method

    if (rpcStatus != null)
    {
        Console.WriteLine($"Google.Rpc Status: Code: {rpcStatus.Code}, Message: {rpcStatus.Message}");

        // Try and get the ErrorInfo from the details
        ErrorInfo? errorInfo = rpcStatus.GetStatusDetails<ErrorInfo>(); // Extension method
        if (errorInfo != null)
        {
            Console.WriteLine($"\tErrorInfo: Reason: {errorInfo.Reason}, Domain: {errorInfo.Domain}");
            foreach (var md in errorInfo.Metadata)
            {
                Console.WriteLine($"\tKey: {md.Key}, Value: {md.Value}");
            }
        }
        // etc, for any other messages expected in the Details ...
    }
}
```

__Example__ - iterating over all the messages in the `Details`:

```C#
void PrintStatusDetails(RpcException ex)
{
    // Get the status from the RpcException
    Google.Rpc.Status? rpcStatus = ex.GetRpcStatus(); // Extension method

    if (rpcStatus != null)
    {
        // Decode each message item in the details in turn
        foreach (var msg in status.UnpackDetailMessage())
        {
            switch (msg)
            {
                case ErrorInfo errorInfo:
                    Console.WriteLine($"ErrorInfo: Reason: {errorInfo.Reason}, Domain: {errorInfo.Domain}");
                    foreach (var md in errorInfo.Metadata)
                    {
                        Console.WriteLine($"\tKey: {md.Key}, Value: {md.Value}");
                    }
                    break;

                case BadRequest badRequest:
                    Console.WriteLine("BadRequest:");
                    foreach (BadRequest.Types.FieldViolation fv in badRequest.FieldViolations)
                    {
                        Console.WriteLine($"\tField: {fv.Field}, Description: {fv.Description}");
                    }
                    break;

                // Other cases handled here ...
            }
        }
    }

```

## Returning errors within gRPC streams

The model described above allows you to return an error status when the gRPC call finishes.

As an extension to the richer error model you may want to allow servers to send back
multiple statuses when streaming responses without terminating the call.

One way of doing this is to include a `google.rpc.Status` message in the definition
of the response messages returned by the server.  The client should also be aware
that it may receive a status in the response.

For example:


```protobuf
service WidgetLookupProvider {
    rpc streamingLookup(stream WidgetReq) returns (stream WidgetRsp) {}
}

message WidgetReq {
    string widget_name = 1;
}

message WidgetRsp {
    oneof message{
        // details when ok
        string widget_details = 1;
        // or error details
        google.rpc.Status status = 2;
   }   
}
```

Note: the  [status.proto](https://github.com/googleapis/googleapis/blob/master/google/rpc/status.proto)
and [error_details.proto](https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto)
files are not currently provided in any NuGet packages. If you wish to use these in
your own message definitions then you will need to copy these into your own project.

Example server code fragment:
```C#
 while (await requestStream.MoveNext())
{
    var request = requestStream.Current;
    var response = new WidgetRsp();

    // ... process the request ...

    // to return an error
    if (error)
    {
        response.Status = new Google.Rpc.Status { /* ... */ };
    } else
    {
        response.WidgetDetails = "the details";
    }
}
```

Example client code fragment:
```C#

// reading the responses
var responseReaderTask = Task.Run(async () =>
{
    while (await call.ResponseStream.MoveNext())
    {
        var rsp = call.ResponseStream.Current;
        switch (rsp.MessageCase)
        {
            case WidgetRsp.MessageOneofCase.WidgetDetails:
                // ... processes the details ...
                break;
            case WidgetRsp.MessageOneofCase.Status:
                // ... handle the error ...
                break;
        }
    }
});

// sending the requests
foreach (var request in requests)
{
    await call.RequestStream.WriteAsync(request);
}
```


## See also
* [gRPC richer error model](https://grpc.io/docs/guides/error/#richer-error-model)
* [Google.Api.CommonProtos](https://cloud.google.com/dotnet/docs/reference/Google.Api.CommonProtos/latest/Google.Api)
