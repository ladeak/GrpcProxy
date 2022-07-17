using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Net.Compression;

namespace GrpcProxy.Grpc;

internal static partial class ProxyPipeExtensions
{
    private const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
    private const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

    private static readonly string MessageCancelledStatus = "Incoming message cancelled.";
    private static readonly string AdditionalDataStatus = "Additional data after the message received.";
    private static readonly string IncompleteMessageStatus = "Incomplete message.";
    private static readonly string ReceivedMessageExceedsLimitStatus =  "Received message exceeds the maximum configured message size.";
    private static readonly string NoMessageEncodingMessageStatus = "Request did not include grpc-encoding value with compressed message.";
    private static readonly string IdentityMessageEncodingMessageStatus = "Request sent 'identity' grpc-encoding value with compressed message.";
    private static string CreateUnknownMessageEncodingMessageStatus(string unsupportedEncoding, IEnumerable<string> supportedEncodings)
    {
        return $"Unsupported grpc-encoding value '{unsupportedEncoding}'. Supported encodings: {string.Join(", ", supportedEncodings)}";
    }

    private static int DecodeMessageLength(ReadOnlySpan<byte> buffer)
    {
        Debug.Assert(buffer.Length >= MessageDelimiterSize, "Buffer too small to decode message length.");

        var result = BinaryPrimitives.ReadUInt32BigEndian(buffer);

        if (result > int.MaxValue)
        {
            throw new IOException("Message too large: " + result);
        }

        return (int)result;
    }

    private static bool TryReadHeader(in ReadOnlySequence<byte> buffer, out bool compressed, out int messageLength)
    {
        if (buffer.Length < HeaderSize)
        {
            compressed = false;
            messageLength = 0;
            return false;
        }

        if (buffer.First.Length >= HeaderSize)
        {
            var headerData = buffer.First.Span.Slice(0, HeaderSize);

            compressed = ReadCompressedFlag(headerData[0]);
            messageLength = DecodeMessageLength(headerData.Slice(1));
        }
        else
        {
            Span<byte> headerData = stackalloc byte[HeaderSize];
            buffer.Slice(0, HeaderSize).CopyTo(headerData);

            compressed = ReadCompressedFlag(headerData[0]);
            messageLength = DecodeMessageLength(headerData.Slice(1));
        }

        return true;
    }

    private static bool ReadCompressedFlag(byte flag)
    {
        if (flag == 0)
        {
            return false;
        }
        else if (flag == 1)
        {
            return true;
        }
        else
        {
            throw new InvalidDataException("Unexpected compressed flag value in message header.");
        }
    }

    public static async Task<T> ReadSingleMessageAsync<T>(this PipeReader input, ProxyHttpContextServerCallContext serverCallContext, Func<DeserializationContext, T> deserializer, MessageDirection direction)
    where T : class
    {
        T? request = null;

        while (true)
        {
            var result = await input.ReadAsync(serverCallContext.CancellationToken);
            var buffer = result.Buffer;

            try
            {
                if (result.IsCanceled)
                    throw new TaskCanceledException("Request cancelled");

                if (!buffer.IsEmpty)
                {
                    if (request != null)
                        throw new InvalidOperationException("Received data after reading single message.");

                    if (TryReadMessage(ref buffer, serverCallContext, direction, out var data))
                    {
                        if (direction == MessageDirection.Request)
                            request = DeserializeRequest(serverCallContext, deserializer, data);
                        else
                            request = DeserializeResponse(serverCallContext, deserializer, data);

                        // Store the request
                        // Need to verify the request completes with no additional data
                    }
                }

                if (result.IsCompleted)
                {
                    if (request != null)
                    {
                        // Additional data came with message
                        if (buffer.Length > 0)
                            throw new InvalidOperationException("Received data after reading single message.");

                        return request;
                    }

                    throw new InvalidOperationException("Data stream ended before receiveing a single message.");
                }
            }
            finally
            {
                // The buffer was sliced up to where it was consumed, so we can just advance to the start.
                if (request != null)
                {
                    input.AdvanceTo(buffer.Start);
                }
                else
                {
                    // We mark examined as buffer.End so that if we didn't receive a full frame, we'll wait for more data
                    // before yielding the read again.
                    input.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }
    }

    public static async ValueTask<T?> ReadStreamMessageAsync<T>(this PipeReader input, ProxyHttpContextServerCallContext serverCallContext, Func<DeserializationContext, T> deserializer, MessageDirection direction, CancellationToken cancellationToken = default)
        where T : class
    {
        while (true)
        {
            var completeMessage = false;
            var result = await input.ReadAsync(cancellationToken);
            var buffer = result.Buffer;

            try
            {
                if (result.IsCanceled)
                    throw new TaskCanceledException("Request cancelled");

                if (!buffer.IsEmpty)
                {
                    if (TryReadMessage(ref buffer, serverCallContext, direction, out var data))
                    {
                        completeMessage = true;
                        T request;
                        if (direction == MessageDirection.Request)
                            request = DeserializeRequest(serverCallContext, deserializer, data);
                        else
                            request = DeserializeResponse(serverCallContext, deserializer, data);
                        return request;
                    }
                }

                if (result.IsCompleted)
                {
                    if (buffer.Length == 0)
                    {
                        // Finished and there is no more data
                        return default;
                    }

                    throw new InvalidOperationException("Data stream ended before receiveing a single message.");
                }
            }
            finally
            {
                // The buffer was sliced up to where it was consumed, so we can just advance to the start.
                if (completeMessage)
                {
                    input.AdvanceTo(buffer.Start);
                }
                else
                {
                    // We mark examined as buffer.End so that if we didn't receive a full frame, we'll wait for more data
                    // before yielding the read again.
                    input.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }
    }

    private static bool TryReadMessage(ref ReadOnlySequence<byte> buffer, ProxyHttpContextServerCallContext context, MessageDirection direction, [NotNullWhen(true)] out ReadOnlySequence<byte>? message)
    {
        if (!TryReadHeader(buffer, out var compressed, out var messageLength))
        {
            message = null;
            return false;
        }

        if (messageLength > context.Options.MaxReceiveMessageSize)
        {
            throw new InvalidOperationException(ReceivedMessageExceedsLimitStatus);
        }

        if (buffer.Length < HeaderSize + messageLength)
        {
            message = null;
            return false;
        }

        // Convert message to byte array
        var messageBuffer = buffer.Slice(HeaderSize, messageLength);

        if (compressed)
        {
            var encoding = GetGrpcEncoding(context, direction);
            if (encoding == null)
            {
                throw new InvalidOperationException(NoMessageEncodingMessageStatus);
            }
            if (GrpcProtocolConstants.IsGrpcEncodingIdentity(encoding))
            {
                throw new InvalidOperationException(IdentityMessageEncodingMessageStatus);
            }

            // Performance improvement would be to decompress without converting to an intermediary byte array
            if (!TryDecompressMessage(encoding, context.Options.CompressionProviders, messageBuffer, out var decompressedMessage))
            {
                // https://github.com/grpc/grpc/blob/master/doc/compression.md#test-cases
                // A message compressed by a client in a way not supported by its server MUST fail with status UNIMPLEMENTED,
                // its associated description indicating the unsupported condition as well as the supported ones. The returned
                // grpc-accept-encoding header MUST NOT contain the compression method (encoding) used.
                var supportedEncodings = new List<string>();
                supportedEncodings.Add(GrpcProtocolConstants.IdentityGrpcEncoding);
                supportedEncodings.AddRange(context.Options.CompressionProviders.Select(p => p.Key));

                throw new InvalidOperationException(CreateUnknownMessageEncodingMessageStatus(encoding, supportedEncodings));
            }

            message = decompressedMessage;
        }
        else
        {
            message = messageBuffer;
        }

        // Update buffer to remove message
        buffer = buffer.Slice(HeaderSize + messageLength);

        return true;
    }

    private static string? GetGrpcEncoding(ProxyHttpContextServerCallContext context, MessageDirection direction)
    {
        if (direction == MessageDirection.Request)
            return context.RequestGrpcEncoding;
        if (direction == MessageDirection.Response)
            return context.ResponseGrpcEncoding;
        return null;
    }

    private static bool TryDecompressMessage(string compressionEncoding, IReadOnlyDictionary<string, ICompressionProvider> compressionProviders, in ReadOnlySequence<byte> messageData, [NotNullWhen(true)] out ReadOnlySequence<byte>? result)
    {
        if (compressionProviders.TryGetValue(compressionEncoding, out var compressionProvider))
        {
            var output = new MemoryStream();
            using (var compressionStream = compressionProvider.CreateDecompressionStream(new ReadOnlySequenceStream(messageData)))
            {
                compressionStream.CopyTo(output);
            }

            result = new ReadOnlySequence<byte>(output.GetBuffer(), 0, (int)output.Length);
            return true;
        }

        result = null;
        return false;
    }

    private static T DeserializeResponse<T>(ProxyHttpContextServerCallContext serverCallContext, Func<DeserializationContext, T> deserializer, ReadOnlySequence<byte>? data) where T : class
    {
        serverCallContext.ResponseDeserializationContext.SetPayload(data);
        T request = deserializer(serverCallContext.ResponseDeserializationContext);
        serverCallContext.ResponseDeserializationContext.SetPayload(null);
        return request;
    }

    private static T DeserializeRequest<T>(ProxyHttpContextServerCallContext serverCallContext, Func<DeserializationContext, T> deserializer, ReadOnlySequence<byte>? data) where T : class
    {
        serverCallContext.RequestDeserializationContext.SetPayload(data);
        T request = deserializer(serverCallContext.RequestDeserializationContext);
        serverCallContext.RequestDeserializationContext.SetPayload(null);
        return request;
    }
}
