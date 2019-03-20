# Migrating managed client types to Grpc.Core.Api

Migrating types for managed client will be different from types required for managed server. The main difference is that there’s a default client base implementation whose functionality must be maintained for back compat. The types `ClientBase<T>` and `ClientBase` has constructors that take in Channel which contains abstractions for making native calls using c core which is then used by the `DefaultCallInvoker`. It is difficult to decouple `ClientBase` and `Channel` while maintaining current functionality. As an alternative, it may be necessary to modify codegen to use a different base class for the client.

To elaborate:
1. Create a new client base class `ManangedClientBase<T>` which doesn’t have the constructor overload that takes in a `Channel` but is otherwise similar to `ClientBase<T>`.
2. The constructor overload that takes in a `CallInvoker` will be used in the managed client implementation
3. Additional types potentially needed include:

* UnimplementedCallInvoker
  * Needed for parameterless  constructor of `ManagedClientBase<T>`
* AsyncClientStreamingCall<TResponse>
* AsyncDuplexStreamingCall<TResponse>
* AsyncServerStreamingCall<TResponse>
* AsyncUnaryCall<TResponse>
* CallInvoker
* IClientStreamWriter<T>
* CallFlags
  * Internal class which may not be needed
* CallCredentials?
  * This type may not be needed as it seems like it’s used to add `Authorization` headers with a per call granularity. We provide functionality through the APIs on `HttpClient` to do this but for portability reasons, though tenuous, we could move this type too. If we do decide to move this type, we need to change the implementation and remove the ToNativeCredentials() method.
  May need to represent this differently in managed clients
* CallOptions?
  * This looks like it’s used for native calls. Maybe we can avoid porting this type if we change the implementation of CallOptions to remove references to this enum. I’d imagine this to be done through a base classe `CallOptionsBase`
* ManagedClientBase?
* ClientBaseConfiguration?
  * Note that ManagedClientBase and ClientBaseConfiguration may not be needed per se. It looks like these types were introduced to provide the `WithHost` functionality. However, I don’t see any usage of these APIs so I’m not sure if we need to port these types.

Major considerations:
* How to configure credentials in managed client
* ClientBase vs ManagedClientBase

I also considered moving native APIs from `Channel` to `ChannelBase` and continue using `ClientBase<T>` but I don’t think this works as well since we would need to change how `DefaultCallInvoker` uses the `ChannelBase` which might require additional configuration to function properly. I can elaborate on this if needed.
