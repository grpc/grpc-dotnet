# Grpc C# API for error handling with Status.proto

This is a protoype NuGet package providing client and server side support for the
[gRPC richer error model](https://grpc.io/docs/guides/error/#richer-error-model).

It had dependencies NuGet packages on:
* `Google.Api.CommonProtos` - to provide the proto implementations used by the richer error model
* `Grpc.Core.Api` - for API classes such as `RpcExcetion`

Reviewers notes:

* The client side borrows from ideas used in [googleapi/gax-dotnet](https://github.com/googleapis/gax-dotnet), specifically [RpcExceptionExtensions.cs](https://github.com/googleapis/gax-dotnet/blob/main/Google.Api.Gax.Grpc/RpcExceptionExtensions.cs)

* The server side uses C#'s Object and Collection initializer syntax. The avoids the needs to a *builder* API to be developed.

## Introduction to the richer error model

Google APIs define an error model that allows a server to return an error status that is more than
just a status code or short message.  The status returned can contain a list of various details giving
more information about the error.

This error model is define by the protocol buffers files [status.proto](https://github.com/googleapis/googleapis/blob/master/google/rpc/status.proto)
and [error_details.proto](https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto)
and is implemented in classes provided in the `Google.Api.CommonProtos` NuGet package.

You can use this same error model in your gRPC services. This package provides classes to support you in
doing this. In this error model the error is encapsulated by an instance of `Google.Rpc.Status` and
returned in the trailing response metadata.  Setting and reading this metadata is handled for you. Details below.

## Server Side

The server side uses C#'s Object and Collection initializer syntax. The avoids the needs to a *builder* API to be developed.

The server returns an error by throwing an `RpcException` that contains metadata with key `"grpc-status-details-bin"` and value that is a serialized `Google.Rpc.Status`.

The `Google.Rpc.Status` can be created and initialized using C#'s Object and Collection initializer syntax. To add messages to the `Details` repeated field, wrap each one in `Any.Pack()` - see example below.

The `Google.Rpc.Status` extension method `ToRpcException` creates the appropriate `RpcException` from the status.

Example:
```C#
throw new Google.Rpc.Status
{
    Code = (int)StatusCode.NotFound,
    Message = "Simple error message",
    Details =
    {
        Any.Pack(new ErrorInfo
        {
            Domain = "Rich Error Model Demo",
            Reason = "Simple error requested in the demo"
        }),
        Any.Pack(new RequestInfo
        {
            RequestId = "EchoRequest",
            ServingData = "Param: " + request.Action.ToString()
        }),
        }
}.ToRpcException();
```

## Client Side

There is an extension method to retrieve a `Google.Rpc.Status` from the metadata in an `RpcException`.  There are also extension methods to retieve the details from the `Google.Rpc.Status`.

If the client knows what details to expect for a specific error code then it can use the extension methods the explicitly extract the know type from the status details. 

Example:
```C#
void PrintError(RpcException ex)
{
    // Get the status from the RpcException
    Google.Rpc.Status? rpcStatus = ex.GetRpcStatus(); // Extension method

    if (rpcStatus != null)
    {
        Console.WriteLine($"Google.Rpc Status: Code: {rpcStatus.Code}, Message: {rpcStatus.Message}");

        // Try and get the ErrorInfo from the details
        ErrorInfo? errorInfo = rpcStatus.GetErrorInfo(); // Extension method
        if (errorInfo != null)
        {
            Console.WriteLine($"\tErrorInfo: Reason: {errorInfo.Reason}, Domain: {errorInfo.Domain}");
            foreach (var md in errorInfo.Metadata)
            {
                Console.WriteLine($"\tKey: {md.Key}, Value: {md.Value}");
            }
        }
        // etc ...
    }
}
```

Alternatively the client can walk the list of details contained in the status, processing
each object. The class `DetailsTypesRegistry` provides an `Unpack` method to decode the `Any` messages in the status details. C#'s switch pattern matching can be used to process the details.

Example:
```C#
void PrintStatusDetails(RpcException ex)
{
    // Get the status from the RpcException
    Google.Rpc.Status? rpcStatus = ex.GetRpcStatus(); // Extension method

    if (rpcStatus != null)
    {
        // Decode each "Any" item from the details in turn
        foreach (Any any in rpcStatus.Details)
        {
            // decode the message if it is one of those expected to be in the
            // status details
            IMessage msg = DetailsTypesRegistry.Unpack(any); 

            switch (msg)
            {
                case null:
                    // ignore
                    Console.WriteLine("Unknown message type in Details");
                    break;

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

The model described above allows you to return an error status when the gRPC call finishes. Sometimes
when using gRPC streams that allow clients and servers to send multiple messages in a single call
you may wish to return a status without terminating the call.

To do this the definition of the stream of messages returned from the service should itself contain
a `google.rpc.Status` message, for example:

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
files are not currently provided in any NuGet packages. The text for those proto files can be copied from
the given links and included in your project.

*TODO* example C# code to follow

## See also
* [Richer error model](https://grpc.io/docs/guides/error/#richer-error-model)
* [Google.Api.CommonProtos](https://cloud.google.com/dotnet/docs/reference/Google.Api.CommonProtos/latest/Google.Api)
