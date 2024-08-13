# BenchmarkWorkerWebsite

Implementation of benchmark worker that is compatible with the gRPC benchmarking stack:
https://grpc.io/docs/guides/benchmarking.html

# FunctionalTestsWebsite

Website that is hosted in TestServer and called by tests in `FunctionalTests`.

# InteropTestsClient

Console app that calls interop tests on `InteropTestsWebsite` and other gRPC implementations. The test client is used by gRPC interop infrastructure. The client can call tests with Grpc.Net.Client and Grpc.Core.

# InteropTestsGrpcWebClient

Blazor WebAssembly app that calls interop tests on `InteropTestsWebsite` and other gRPC implementations using gRPC-Web. The Blazor client app is hosted by `InteropTestsGrpcWebWebsite`.

# InteropTestsGrpcWebWebsite

ASP.NET Core app that hosts `InteropTestsGrpcWebClient`.

Start the website by running it in Docker. Execute in the root of repository:

```
docker compose -f docker-compose.yml build grpcweb-client
docker compose -f docker-compose.yml up grpcweb-client
```

gRPC-Web interop client is hosted at `http://localhost:8081`.

The *testassets/InteropTestsGrpcWebWebsite/Tests* directory contains scripts for automatically executing the interop tests. Steps to run the tests:

1. Start `InteropTestsWebsite` container
2. Start `InteropTestsGrpcWebWebsite` container
3. In *testassets/InteropTestsGrpcWebWebsite/Tests* call `npm test`

# InteropTestsNativeServer

A copy of [InteropServer.cs](https://github.com/grpc/grpc/blob/912653e3ce504b9148409e577bc2028c4454d89c/src/csharp/Grpc.IntegrationTesting/InteropServer.cs) from https://github.com/grpc/grpc. The C# C Core native server is copied here to allow easier initial testing of the managed gRPC client against C Core. It could be removed from this repo in the future. The authoritative copy of the interop tests is https://github.com/grpc/grpc. This copy may become out of date.

# InteropTestsWebsite

ASP.NET Core app that hosts gRPC interop service. It is called by `InteropTestsClient`, `InteropTestsGrpcWebClient` and other gRPC client implementations. The interop service is used by gRPC interop infrastucture.

Start the website by running it in Docker. Execute in the root of repository:

```
docker compose -f docker-compose.yml build grpcweb-server
docker compose -f docker-compose.yml up grpcweb-server
```

gRPC interop services are hosted at `http://localhost:8080`.
