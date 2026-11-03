using System.Net;
using System.Net.Sockets;
using DaxDiff.Domain;
using System.Globalization;
using DaxDiff.Connectors.Core.Resources;

namespace DaxDiff.Connectors;

/// <summary>Maps library exceptions to actionable <see cref="DaxDiffException"/>s (FR-015, AT-08).</summary>
public static class ErrorClassifier
{
    public static DaxDiffException Classify(Exception ex, string transport)
    {
        if (ex is DaxDiffException known)
            return known;

        var message = Flatten(ex);
        var lower = message.ToLowerInvariant();

        if (ex is TimeoutException || lower.Contains("timed out") || lower.Contains("timeout expired"))
            return new DaxDiffException(ErrorCategory.Timeout, "QRY-TIMEOUT", string.Format(CultureInfo.CurrentCulture, Strings.Error_Timeout, transport),
                Strings.Remedy_Timeout, isTransient: true, inner: ex);

        if (lower.Contains("xmla") && (lower.Contains("disabled") || lower.Contains("not enabled") || lower.Contains("not supported") || lower.Contains("read only")))
            return new DaxDiffException(ErrorCategory.UnsupportedModel, "CON-XMLA-DISABLED",
                Strings.Error_XmlaDisabled,
                Strings.Remedy_XmlaDisabled, inner: ex);

        if (lower.Contains("premium") && lower.Contains("capacity"))
            return new DaxDiffException(ErrorCategory.UnsupportedModel, "CON-CAPACITY",
                Strings.Error_Capacity,
                Strings.Remedy_Capacity, inner: ex);

        // The service gives the same answer for a wrong name, a workspace the account cannot see and one without capacity.
        if (lower.Contains("workspace") && lower.Contains("not found"))
            return new DaxDiffException(ErrorCategory.Configuration, "CON-WORKSPACE-NOT-FOUND",
                string.Format(CultureInfo.CurrentCulture, Strings.Error_WorkspaceNotFound, transport),
                Strings.Remedy_WorkspaceNotFound, inner: ex);

        if (lower.Contains("permission") || lower.Contains("not authorized") || lower.Contains("unauthorized") || lower.Contains("access is denied") || lower.Contains("forbidden"))
            return new DaxDiffException(ErrorCategory.Authorization, "CON-PERMISSION",
                Strings.Error_Permission,
                Strings.Remedy_Permission, inner: ex);

        if (lower.Contains("aadsts") || (lower.Contains("token") && lower.Contains("expired")))
            return new DaxDiffException(ErrorCategory.Authentication, "AUTH-EXPIRED", Strings.Error_SignInExpired,
                Strings.Remedy_SignInAgain, inner: ex);

        if (ex is SocketException || ex is HttpRequestException { StatusCode: null } ||
            lower.Contains("no connection could be made") || lower.Contains("a connection cannot be made") ||
            lower.Contains("cannot connect") || lower.Contains("network"))
            return new DaxDiffException(ErrorCategory.Network, "CON-NETWORK", string.Format(CultureInfo.CurrentCulture, Strings.Error_Network, transport),
                Strings.Remedy_Network,
                isTransient: true, inner: ex);

        if (lower.Contains("query (") || lower.Contains("syntax") || lower.Contains("cannot be found") || lower.Contains("cannot find") ||
            lower.Contains("dax") || lower.Contains("failed to resolve"))
            return new DaxDiffException(ErrorCategory.InvalidQuery, "QRY-INVALID", string.Format(CultureInfo.CurrentCulture, Strings.Error_InvalidQuery, Trim(message)),
                Strings.Remedy_InvalidQuery, inner: ex);

        return new DaxDiffException(ErrorCategory.Unknown, "ERR-UNEXPECTED", $"{transport}: {Trim(message)}",
            Strings.Remedy_Unexpected, inner: ex);
    }

    public static DaxDiffException FromHttpStatus(HttpStatusCode status, string body)
    {
        var detail = Trim(body);
        return status switch
        {
            HttpStatusCode.Unauthorized => new DaxDiffException(ErrorCategory.Authentication, "REST-401",
                Strings.Rest_Unauthorized, Strings.Remedy_SignInAgain),
            HttpStatusCode.Forbidden => new DaxDiffException(ErrorCategory.Authorization, "REST-403",
                Strings.Rest_Forbidden,
                Strings.Remedy_RestForbidden),
            HttpStatusCode.NotFound => new DaxDiffException(ErrorCategory.Configuration, "REST-404",
                Strings.Rest_NotFound, Strings.Remedy_RestNotFound),
            HttpStatusCode.TooManyRequests => new DaxDiffException(ErrorCategory.Throttled, "REST-429",
                Strings.Rest_Throttled, Strings.Remedy_RestThrottled, isTransient: true),
            HttpStatusCode.BadRequest => new DaxDiffException(ErrorCategory.InvalidQuery, "REST-400",
                string.Format(CultureInfo.CurrentCulture, Strings.Rest_QueryRejected, detail), Strings.Remedy_RestBadRequest),
            >= HttpStatusCode.InternalServerError => new DaxDiffException(ErrorCategory.Network, $"REST-{(int)status}",
                Strings.Rest_ServerError, Strings.Remedy_RetryLater, isTransient: true),
            _ => new DaxDiffException(ErrorCategory.Unknown, $"REST-{(int)status}", string.Format(CultureInfo.CurrentCulture, Strings.Rest_UnexpectedResponse, detail), Strings.Remedy_Retry),
        };
    }

    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
            parts.Add(e.Message);
        return string.Join(" | ", parts);
    }

    private static string Trim(string text) => text.Length <= 400 ? text : text[..400] + "…";
}
