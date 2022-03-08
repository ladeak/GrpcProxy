using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace GrpcProxy.Forwarder;

public static class RequestUtilities
{
    private static readonly HashSet<string> _contentHeaders = new(11, StringComparer.OrdinalIgnoreCase)
    {
        HeaderNames.Allow,
        HeaderNames.ContentDisposition,
        HeaderNames.ContentEncoding,
        HeaderNames.ContentLanguage,
        HeaderNames.ContentLength,
        HeaderNames.ContentLocation,
        HeaderNames.ContentMD5,
        HeaderNames.ContentRange,
        HeaderNames.ContentType,
        HeaderNames.Expires,
        HeaderNames.LastModified
    };

    private static readonly HashSet<string> _headersToExclude = new(22, StringComparer.OrdinalIgnoreCase)
    {
        HeaderNames.Connection,
        HeaderNames.TransferEncoding,
        HeaderNames.KeepAlive,
        HeaderNames.Upgrade,
        "Proxy-Connection",
        "Proxy-Authenticate",
        "Proxy-Authentication-Info",
        "Proxy-Authorization",
        "Proxy-Features",
        "Proxy-Instruction",
        "Security-Scheme",
        "ALPN",
        "Close",
        "HTTP2-Settings",
        HeaderNames.UpgradeInsecureRequests,
        HeaderNames.TE,
        HeaderNames.AltSvc,
    };

    internal static HttpMethod GetHttpMethod(string method) => method switch
    {
        string mth when HttpMethods.IsGet(mth) => HttpMethod.Get,
        string mth when HttpMethods.IsPost(mth) => HttpMethod.Post,
        string mth when HttpMethods.IsPut(mth) => HttpMethod.Put,
        string mth when HttpMethods.IsDelete(mth) => HttpMethod.Delete,
        string mth when HttpMethods.IsOptions(mth) => HttpMethod.Options,
        string mth when HttpMethods.IsHead(mth) => HttpMethod.Head,
        string mth when HttpMethods.IsPatch(mth) => HttpMethod.Patch,
        string mth when HttpMethods.IsTrace(mth) => HttpMethod.Trace,
        string mth when HttpMethods.IsConnect(mth) => throw new NotSupportedException($"Unsupported request method '{method}'."),
        _ => new HttpMethod(method)
    };

    /// <summary>
    /// Appends the given path and query to the destination prefix while avoiding duplicate '/'.
    /// </summary>
    /// <param name="destinationPrefix">The scheme, host, port, and optional path base for the destination server.
    /// e.g. "http://example.com:80/path/prefix"</param>
    /// <param name="path">The path to append.</param>
    /// <param name="query">The query to append</param>
    public static Uri MakeDestinationAddress(string destinationPrefix, PathString path, QueryString query)
    {
        ReadOnlySpan<char> prefixSpan = destinationPrefix;

        if (path.HasValue && destinationPrefix.EndsWith('/'))
        {
            // When PathString has a value it always starts with a '/'. Avoid double slashes when concatenating.
            prefixSpan = prefixSpan[0..^1];
        }

        var targetAddress = string.Concat(prefixSpan, EncodePath(path), query.ToUriComponent());

        return new Uri(targetAddress, UriKind.Absolute);
    }

    // This isn't using PathString.ToUriComponent() because it doesn't round trip some escape sequences the way we want.
    private static string EncodePath(PathString path)
    {
        if (!path.HasValue)
        {
            return string.Empty;
        }

        // Check if any escaping is required.
        var value = path.Value!;
        for (var i = 0; i < value.Length; i++)
        {
            if (!IsValidPathChar(value[i]))
            {
                return EncodePath(value, i);
            }
        }

        return value;
    }

    private static string EncodePath(string value, int i)
    {
        StringBuilder? buffer = null;

        var start = 0;
        var count = i;
        var requiresEscaping = false;

        while (i < value.Length)
        {
            if (IsValidPathChar(value[i]))
            {
                if (requiresEscaping)
                {
                    // the current segment requires escape
                    buffer ??= new StringBuilder(value.Length * 3);
                    buffer.Append(Uri.EscapeDataString(value.Substring(start, count)));

                    requiresEscaping = false;
                    start = i;
                    count = 0;
                }

                count++;
                i++;
            }
            else
            {
                if (!requiresEscaping)
                {
                    // the current segment doesn't require escape
                    buffer ??= new StringBuilder(value.Length * 3);
                    buffer.Append(value, start, count);

                    requiresEscaping = true;
                    start = i;
                    count = 0;
                }

                count++;
                i++;
            }
        }

        if (count == value.Length && !requiresEscaping)
        {
            return value;
        }
        else
        {
            if (count > 0)
            {
                buffer ??= new StringBuilder(value.Length * 3);

                if (requiresEscaping)
                {
                    buffer.Append(Uri.EscapeDataString(value.Substring(start, count)));
                }
                else
                {
                    buffer.Append(value, start, count);
                }
            }

            return buffer?.ToString() ?? string.Empty;
        }
    }

    // https://datatracker.ietf.org/doc/html/rfc3986/#appendix-A
    // pchar         = unreserved / pct-encoded / sub-delims / ":" / "@"
    // pct-encoded   = "%" HEXDIG HEXDIG
    // unreserved    = ALPHA / DIGIT / "-" / "." / "_" / "~"
    // reserved      = gen-delims / sub-delims
    // gen-delims    = ":" / "/" / "?" / "#" / "[" / "]" / "@"
    // sub-delims    = "!" / "$" / "&" / "'" / "(" / ")" / "*" / "+" / "," / ";" / "="

    // uint[] bits uses 1 cache line (Array info + 16 bytes)
    // bool[] would use 3 cache lines (Array info + 128 bytes)
    // So we use 128 bits rather than 128 bytes/bools
    private static readonly uint[] ValidPathChars = {
        0b_0000_0000__0000_0000__0000_0000__0000_0000, // 0x00 - 0x1F
        0b_0010_1111__1111_1111__1111_1111__1101_0010, // 0x20 - 0x3F
        0b_1000_0111__1111_1111__1111_1111__1111_1111, // 0x40 - 0x5F
        0b_0100_0111__1111_1111__1111_1111__1111_1110, // 0x60 - 0x7F
    };

    internal static bool IsValidPathChar(char c)
    {
        // Use local array and uint .Length compare to elide the bounds check on array access
        var validChars = ValidPathChars;
        var i = (int)c;

        // Array is in chunks of 32 bits, so get offset by dividing by 32
        var offset = i >> 5; // i / 32;
        // Significant bit position is the remainder of the above calc; i % 32 => i & 31
        var significantBit = 1u << (i & 31);

        // Check offset in bounds and check if significant bit set
        return (uint)offset < (uint)validChars.Length &&
            ((validChars[offset] & significantBit) != 0);
    }

    // Note: HttpClient.SendAsync will end up sending the union of
    // HttpRequestMessage.Headers and HttpRequestMessage.Content.Headers.
    // We don't really care where the proxied headers appear among those 2,
    // as long as they appear in one (and only one, otherwise they would be duplicated).
    // Some headers may only appear on HttpContentHeaders, in which case we inject
    // an EmptyHttpContent - dummy 0-length container only used for headers.
    internal static void AddHeader(HttpRequestMessage request, string headerName, StringValues value)
    {
        if (value.Count == 1)
        {
            string headerValue = value;

            if (ContainsNewLines(headerValue))
            {
                return;
            }

            if (!request.Headers.TryAddWithoutValidation(headerName, headerValue))
            {
                if (request.Content is null && _contentHeaders.Contains(headerName))
                {
                    request.Content = new EmptyHttpContent();
                }

                var added = request.Content?.Headers.TryAddWithoutValidation(headerName, headerValue);
            }
        }
        else
        {
            string[] headerValues = value;

            // HttpClient wrongly uses comma (",") instead of semi-colon (";") as a separator for Cookie headers.
            // To mitigate this, we concatenate them manually and put them back as a single header value.
            // A multi-header cookie header is invalid, but we get one because of
            // https://github.com/dotnet/aspnetcore/issues/26461
            if (string.Equals(headerName, HeaderNames.Cookie, StringComparison.OrdinalIgnoreCase))
            {
                AddHeader(request, headerName, string.Join("; ", headerValues));
                return;
            }

            foreach (var headerValue in headerValues)
            {
                if (ContainsNewLines(headerValue))
                {
                    return;
                }
            }

            if (!request.Headers.TryAddWithoutValidation(headerName, headerValues))
            {
                if (request.Content is null && _contentHeaders.Contains(headerName))
                {
                    request.Content = new EmptyHttpContent();
                }

                var added = request.Content?.Headers.TryAddWithoutValidation(headerName, headerValues);
            }
        }

        static bool ContainsNewLines(string value) => value.AsSpan().IndexOfAny('\r', '\n') >= 0;
    }

    internal static bool ContainsHeader(HttpHeaders headers, string headerName)
    {
#if NET6_0_OR_GREATER
        return headers.NonValidated.Contains(headerName);
#else
        return headers.TryGetValues(headerName, out _);
#endif
    }

    internal static bool ShouldSkipRequestHeader(string headerName)
    {
        if (_headersToExclude.Contains(headerName))
        {
            return true;
        }

        // Filter out HTTP/2 pseudo headers like ":method" and ":path", those go into other fields.
        if (headerName.StartsWith(':'))
        {
            return true;
        }

        return false;
    }

    internal static bool ShouldSkipResponseHeader(string headerName)
    {
        return _headersToExclude.Contains(headerName);
    }

    internal static bool TryGetValues(HttpHeaders headers, string headerName, out StringValues values)
    {
#if NET6_0_OR_GREATER
        if (headers.NonValidated.TryGetValues(headerName, out var headerStringValues))
        {
            if (headerStringValues.Count <= 1)
            {
                values = headerStringValues.ToString();
            }
            else
            {
                values = ToArray(headerStringValues);
            }
            return true;
        }

        static StringValues ToArray(in HeaderStringValues values)
        {
            var array = new string[values.Count];
            var i = 0;
            foreach (var value in values)
            {
                array[i++] = value;
            }
            return array;
        }
#else
        if (headers.TryGetValues(headerName, out var headerValues))
        {
            values = headerValues as string[] ?? headerValues.ToArray();
            return true;
        }
#endif

        values = default;
        return false;
    }

#if NET6_0_OR_GREATER
    internal static StringValues Concat(in StringValues existing, in HeaderStringValues values)
    {
        if (values.Count <= 1)
        {
            return StringValues.Concat(existing, values.ToString());
        }
        else
        {
            return ConcatSlow(existing, values);
        }

        static StringValues ConcatSlow(in StringValues existing, in HeaderStringValues values)
        {
            var count = existing.Count;
            var newArray = new string[count + values.Count];

            if (count == 1)
            {
                newArray[0] = existing.ToString();
            }
            else
            {
                existing.ToArray().CopyTo(newArray, 0);
            }

            foreach (var value in values)
            {
                    newArray[count++] = value;
            }

            return newArray;
        }
    }
#endif
}
