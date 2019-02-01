# gRPC ASP.NET Core packages

## Overview

![image](images/packages.png)

## Design considerations

### Grpc.AspNetCore

We envision shipping both a managed client and managed server. Since gRPC services often act as both client and server, we want to make the consumption of both components as frictionless as possible. In addition to the server and client packages, the metapackage will also take dependencies on tooling support (code gen from Grpc.Tools and ServiceReference support from Microsoft.Extensions.ApiDescription.Design) and the default message protocol (Google.Protobuf).

We want to distinguish these from the native clients and server so the package names will be prefixed with Grpc.AspNetCore/Grpc.NetCore. We don't expect most users to take a direct dependency on these packages so we will use a three-part name.

We are committed to delivering the managed server experience Microsoft.AspNetCore.Server functionalities in ASP.NET Core 3.0 timeframe. We will strive to also deliver the mananged client experience in 3.0.

### Namespaces

Types in the Grpc.Core.Api package will use the same namespaces of their original types in Grpc.Core to retain full API back-compatibility. Type forwarding from Grpc.Core to Grpc.Core.Api will also be used to ensure ABI back-compatibility.

Public Types in the Grpc.AspNetCore.Server will be included in the Grpc.AspNetCore.Server namespace with exceptions for certain APIs which by convention uses other namespaces (for example, ServiceExtensions belong to the Microsoft.Extensions.DependencyInjection namespace).

By convention, internal types will be included in the {PackageName}.Internal namespace.

### Grpc.Core.Api

To implement the managed server, we need to have access to the APIs in Grpc.Core. However, the ASP.NET Core implementation aims to be purely managed with no native dependencies. This will require the extractions of APIs from Grpc.Core to Grpc.Core.Api. The full list of types we aim to include in Grpc.Core.Api is as follows:

```
Grpc.Core.AuthContext
Grpc.Core.AuthProperty
Grpc.Core.ClientStreamingServerMethod`2
Grpc.Core.ContextPropagationFlags
Grpc.Core.ContextPropagationOptions
Grpc.Core.ContextPropagationToken
Grpc.Core.DeserializationContext
Grpc.Core.DuplexStreamingServerMethod`2
Grpc.Core.IAsyncStreamReader`1
Grpc.Core.IAsyncStreamWriter`1
Grpc.Core.IHasWriteOptions
Grpc.Core.IMethod
Grpc.Core.Internal.IServerCallHandler
Grpc.Core.Internal.MarshalUtils
Grpc.Core.IServerStreamWriter`1
Grpc.Core.Logging.ILogger
Grpc.Core.Logging.LogLevel
Grpc.Core.Marshaller`1
Grpc.Core.Metadata
Grpc.Core.Metadata+Entry
Grpc.Core.Method`2
Grpc.Core.MethodType
Grpc.Core.RpcException
Grpc.Core.SerializationContext
Grpc.Core.ServerCallContext
Grpc.Core.ServerServiceDefinition
Grpc.Core.ServerStreamingServerMethod`2
Grpc.Core.ServiceBinderBase
Grpc.Core.Status
Grpc.Core.StatusCode
Grpc.Core.UnaryServerMethod`2
Grpc.Core.Utils.GrpcPreconditions
Grpc.Core.WriteFlags
Grpc.Core.WriteOptions
```
