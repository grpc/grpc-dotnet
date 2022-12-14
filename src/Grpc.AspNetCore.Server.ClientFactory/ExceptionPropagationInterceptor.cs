using System;
using Grpc.Core;
using Newtonsoft.Json;
using Grpc.AspNetCore.Server;
using Grpc.Core.Interceptors;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.ClientFactory;

internal class ExceptionPropagationInterceptor : Interceptor
{
	/// <inheritdoc/>
	public override TResponse BlockingUnaryCall<TRequest, TResponse>(TRequest request,
		ClientInterceptorContext<TRequest, TResponse> context,
		BlockingUnaryCallContinuation<TRequest, TResponse> continuation) 
		
		=> ExecuteCallCore(() =>
		{
			return base.BlockingUnaryCall(request, context, continuation);
		});

	/// <inheritdoc/>
	public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request,
		ClientInterceptorContext<TRequest, TResponse> context,
		AsyncUnaryCallContinuation<TRequest, TResponse> continuation) 
		
		=> ExecuteCallCore(() =>
		{
			return base.AsyncUnaryCall(request, context, continuation);
		});

	/// <inheritdoc/>
	public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(TRequest request,
		ClientInterceptorContext<TRequest, TResponse> context,
		AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation) 
		
		=> ExecuteCallCore(() =>
		{
			return base.AsyncServerStreamingCall(request, context, continuation);
		});

	/// <inheritdoc/>
	public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context,
			AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
		
		=> ExecuteCallCore(() =>
		{
			return base.AsyncClientStreamingCall(context, continuation);
		});

	/// <inheritdoc/>
	public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context,
		AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
		
		=> ExecuteCallCore(() =>
		{
			return base.AsyncDuplexStreamingCall(context, continuation);
		});


	/// <inheritdoc/>
	public override Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
		IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
		DuplexStreamingServerMethod<TRequest, TResponse> continuation)

		=> ExecuteCallCore(() =>
		{
			return base.DuplexStreamingServerHandler(requestStream, responseStream, context, continuation);
		});

	private static TReturn ExecuteCallCore<TReturn>(Func<TReturn> continuation)
		where TReturn : class
	{
		try
		{
			return continuation();
		}
		catch (RpcException rpcEx) when (rpcEx.Status.StatusCode == StatusCode.Application)
		{
			var applicationException = DeserializeApplicationException(rpcEx.Trailers);
			if (applicationException != null)
			{
				throw applicationException;
			}
			throw;
		}
	}

	private static Exception? DeserializeApplicationException(Metadata metadata)
	{
		if (!TryGetExceptionInfoFromMetadata(metadata, out ExceptionMetadata exceptionMetadata))
			return null;

		return JsonConvert.DeserializeObject(exceptionMetadata.Data, exceptionMetadata.Type) as Exception;
	}

	private static bool TryGetExceptionInfoFromMetadata(Metadata metadata, out ExceptionMetadata exceptionMetadata)
	{
		exceptionMetadata = default;
		var exceptionMetadataValue = metadata?.GetValue(ExceptionMetadata.PropagatedExceptionMetadataKey);

		if (string.IsNullOrWhiteSpace(exceptionMetadataValue))
			return false;

		exceptionMetadata = JsonConvert.DeserializeObject<ExceptionMetadata>(exceptionMetadataValue);

		return exceptionMetadata.Type != null
			&& exceptionMetadata.Data != null
			&& exceptionMetadata.Data.Length > 0;
	}
}