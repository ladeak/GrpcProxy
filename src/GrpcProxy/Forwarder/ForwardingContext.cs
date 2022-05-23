namespace GrpcProxy.Forwarder;

internal record ForwardingContext(HttpResponseMessage ResponseMessage, StreamCopyHttpContent? StreamCopyContent);