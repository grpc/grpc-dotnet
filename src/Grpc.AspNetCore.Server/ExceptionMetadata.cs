using System;
using Grpc.Core;
using Grpc.Core.Utils;

namespace Grpc.AspNetCore.Server;

/// <summary>
/// Represents the metadata for an <see cref="Exception"/>.
/// </summary>
public struct ExceptionMetadata
{
	/// <summary>
	/// The key used to store the exception in the <see cref="Metadata"/>.
	/// </summary>
	public const string PropagatedExceptionMetadataKey = "PropagatedException";

	/// <summary>
	/// Gets the type of the exception.
	/// </summary>
	public Type Type { get; private set; }

	/// <summary>
	/// Gets the exception serialized in form of array of bytes.
	/// </summary>
	public string Data { get; private set; }

	/// <summary>
	/// Initiliazes a new instance of the <see cref="ExceptionMetadata"/> class.
	/// </summary>
	/// <param name="type">
	/// The type of the exception.
	/// </param>
	/// <param name="data">
	/// The serialized exception.
	/// </param>
	public ExceptionMetadata(Type type, string data)
	{
		Type = GrpcPreconditions.CheckNotNull(type, nameof(type));
		Data = GrpcPreconditions.CheckNotNull(data, nameof(data));
	}
}