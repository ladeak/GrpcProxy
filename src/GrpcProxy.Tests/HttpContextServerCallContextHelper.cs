using Grpc.Core;
using GrpcProxy.Grpc;
using Microsoft.AspNetCore.Http;
using System.IO.Compression;
using Grpc.Net.Compression;
using Grpc.AspNetCore.Server;
using Grpc.Shared.Server;

namespace GrpcProxy.Tests;

internal static class HttpContextServerCallContextHelper
{
    public static ProxyHttpContextServerCallContext CreateServerCallContext(
        HttpContext? httpContext = null,
        List<ICompressionProvider>? compressionProviders = null,
        string? responseCompressionAlgorithm = null,
        CompressionLevel? responseCompressionLevel = null,
        int? maxSendMessageSize = null,
        int? maxReceiveMessageSize = null,
        WriteOptions? writeOptions = null,
        bool initialize = true)
    {
        var options = CreateMethodOptions(
            compressionProviders,
            responseCompressionAlgorithm,
            responseCompressionLevel,
            maxSendMessageSize,
            maxReceiveMessageSize);

        var context = new ProxyHttpContextServerCallContext(
            httpContext ?? new DefaultHttpContext(),
            options,
            typeof(object),
            typeof(object));
        if (writeOptions != null)
        {
            context.WriteOptions = writeOptions;
        }
        if (initialize)
        {
            context.Initialize();
        }

        return context;
    }

    public static MethodOptions CreateMethodOptions(
        List<ICompressionProvider>? compressionProviders = null,
        string? responseCompressionAlgorithm = null,
        CompressionLevel? responseCompressionLevel = null,
        int? maxSendMessageSize = null,
        int? maxReceiveMessageSize = null)
    {
        var serviceOptions = new GrpcServiceOptions();
        serviceOptions.CompressionProviders = compressionProviders ?? new List<ICompressionProvider>();
        serviceOptions.MaxSendMessageSize = maxSendMessageSize;
        serviceOptions.MaxReceiveMessageSize = maxReceiveMessageSize;
        serviceOptions.ResponseCompressionAlgorithm = responseCompressionAlgorithm;
        serviceOptions.ResponseCompressionLevel = responseCompressionLevel;

        return MethodOptions.Create(new[] { serviceOptions });
    }
}
