# gRPC 5.0 planning

| Legend |                          |
|--------|--------------------------|
| 🔴      | Stretch goal |
| ❔       | Needs more info |

## Runtime

- Linker-friendly gRPC
  - As part of positioning .NET as a viable technology for cloud native, we should ensure we are linker-friendly and audit any possible future usage of reflection/ref-emit
  - Optimize for working-set memory/published output size
    - See https://github.com/rynowak/link-a-thon/blob/master/Findings.md for comparison

  - Additional Server
    - HttpSysServer
    - IIS In-proc

- Introduce additional transports on the server
  - Unix-domain sockets
  - Named pipes (on Windows)
  - 🔴 QUIC

- Introduce additional transports on the client
  - Unix-domain sockets
  - Named pipes (on Windows)
  - 🔴 QUIC

- Serializer/de-serializer performance improvements
  - https://github.com/protocolbuffers/protobuf/pull/5888

- HTTP/2 performance on the server
  - HPACK Static dictionary
  - HPACK Dynamic dictionary
  - Pooling of HTTP/2 Streams
  - 🔴 Window update tuning

 - Ensure code-first gRPC is successful
  - Marc Gravell is using protobuf.net to build a code-first gRPC experience on grpc-dotnet. Ensure our abstractions allow him to be successful.

 - Ensure WCF to gRPC ebook is successful
  - Provide SME guidance where applicable

- Testing existing client libraries on both implementations
    -	There are APIs and SDKs to share between the implementations
    -	@Jan Tattermusch you mentioned that you have a doc noting the differences that you can share?

- Load balancing
    -	Integration with XDS load balancing APIs
    -	May need Connectivity APIs work in HttpClient

- Connection Features
    -	Keep alive
    -	Connection idle
    -	Wait for ready
    -	These features are often needed for production ready services

- Support status.proto
    -	Trailing rich metadata for error details
    -	Works across different implementation

- Channelz support
    -	Observability of server status


## Tooling

- 🔴 Build a LSP for protobuf
- 🔴 VS Code extension to consume LSP
  - ❔ Possibly partner with VS Code team here
- 🔴 VS Extension to consume LSP

- Tooling for working with gRPC services without building a client project
  - Integration with HttpREPL or something like WCFTestClient?

- Generating and working with client certificates

- Publishing to AKS

## Ecosystem

- Envoy integration
  - 🔴 Integration with Envoy XDS APIs for client-side load balancing
- Kubernetes/AKS
  - ❔ Helm charts for CLI publish
- Xamarin apps support

## Documentation/Samples

- OAuth E2E
- More AuthN/AuthZ coverage
- gRPC specific features
  - Deadlines
  - Cancellation
- Distributed Tracing
  - OpenCenus/Open Telemetry
- Interceptors

## Intentional cuts/omissions

- gRPC web
- gRPC with JSON (as opposed to protobuf)
