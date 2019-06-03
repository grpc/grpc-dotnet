# .NET client for gRPC

This is an experimental .NET client for gRPC. This client uses `HttpClient` instead of C-core to make calls to gRPC servers.

**WARNING** - The client is not ready for production usage. It does not support reading status and trailers, and because of this the client can't recognize when the server reports a failure.