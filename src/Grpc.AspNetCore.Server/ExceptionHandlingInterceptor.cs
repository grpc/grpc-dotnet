using System;
using Grpc.Core;
using Newtonsoft.Json;
using System.Reflection;
using Grpc.Core.Interceptors;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Server;

/// <summary>
/// Adds to the gRpc pipeline the functionality to propagate custom (or application) exceptions.
/// </summary>
public class ExceptionHandlingInterceptor : Interceptor
{
	/// <inheritdoc/>
	public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request,
		ServerCallContext context,
		UnaryServerMethod<TRequest, TResponse> continuation)

		=> await ServerHandlerCore(async () =>
		{
			return await base.UnaryServerHandler(request, context, continuation);
		});

	/// <inheritdoc/>
	public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
		ServerCallContext context,
		ClientStreamingServerMethod<TRequest, TResponse> continuation)

		=> await ServerHandlerCore(async () =>
		{
			return await base.ClientStreamingServerHandler(requestStream, context, continuation);
		});

	/// <inheritdoc/>
	public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
		IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
		DuplexStreamingServerMethod<TRequest, TResponse> continuation)

		=> await ServerHandlerCore<Func<Task>>(async () =>
		{
			await base.DuplexStreamingServerHandler(requestStream, responseStream, context, continuation);
		});

	/// <inheritdoc/>
	public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request,
		IServerStreamWriter<TResponse> responseStream,
		ServerCallContext context,
		ServerStreamingServerMethod<TRequest, TResponse> continuation)

		=> await ServerHandlerCore<Func<Task>>(async () =>
		{
			await base.ServerStreamingServerHandler(request, responseStream, context, continuation);
		});

	private static async Task<TResult> ServerHandlerCore<TResult>(Func<Task<TResult>> continuation)
		where TResult : class
	{
		try
		{
			return await continuation();
		}
		catch (Exception ex) when (ex.GetType() != typeof(RpcException))
		{
			var exceptionToRethrown = ConvertExceptionToRpcException(ex) ?? ex;
			throw exceptionToRethrown;
		}
	}

	private static async Task ServerHandlerCore<TResult>(Func<Task> continuation)
	{
		try
		{
			await continuation();
		}
		catch (Exception ex) when (ex.GetType() != typeof(RpcException))
		{
			var exceptionToRethrown = ConvertExceptionToRpcException(ex) ?? ex;
			throw exceptionToRethrown;
		}
	}

	private static RpcException ConvertExceptionToRpcException(Exception exception)
	{
		var metadata = new Metadata();
		var exceptionMetadata = ConvertExceptionToMetadata(exception);
		var serializedExceptionContent = JsonConvert.SerializeObject(exceptionMetadata);
		metadata.Add(new Metadata.Entry(ExceptionMetadata.PropagatedExceptionMetadataKey, serializedExceptionContent));

		return new RpcException(new Status(StatusCode.Application, ExceptionMetadata.PropagatedExceptionMetadataKey), metadata);
	}

	private static ExceptionMetadata ConvertExceptionToMetadata(Exception exception)
	{
		return new ExceptionMetadata
		(
			exception.GetType(),
			JsonConvert.SerializeObject(exception, Formatting.Indented)
		);
	}
}