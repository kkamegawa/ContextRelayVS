using System;
using System.Net;

namespace ContextRelay.Core.Adapters;

public sealed class GraphApiException : Exception
{
    public GraphApiException(
        HttpStatusCode statusCode,
        string message,
        string? errorCode = null,
        string? requestId = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        RequestId = requestId;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ErrorCode { get; }

    public string? RequestId { get; }

    public bool LicenseRequired =>
        string.Equals(ErrorCode, "CopilotLicenseRequired", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ErrorCode, "licenseRequired", StringComparison.OrdinalIgnoreCase);
}
