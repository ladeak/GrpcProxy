﻿using System.Net;

namespace GrpcProxy.Forwarder;

internal sealed class EmptyHttpContent : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => Task.CompletedTask;

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return true;
    }
}