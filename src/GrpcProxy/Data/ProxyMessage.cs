using System.Net;

namespace GrpcProxy.Data;

public record ProxyMessage(Guid ProxyCallId,
    MessageDirection Direction,
    DateTime Timestamp,
    string Endpoint,
    List<string> Headers,
    string Path, string Message,
    string MethodType,
    bool IsCancelled,
    HttpStatusCode? StatusCode = null,
    Exception? ProxyError = null) : ISearchable
{
    public bool Contains(string text)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        return Endpoint == text
            || Path == text
            || MethodType == text
            || text == "Request" && Direction == MessageDirection.Request
            || text == "Response" && Direction == MessageDirection.Response
            || text == "Proxy Error" && Direction == MessageDirection.None
            || Message.Contains(text)
            || (ProxyError?.Message.Contains(text) ?? false);
    }
}
