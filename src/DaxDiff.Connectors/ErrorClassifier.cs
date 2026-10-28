using System.Net;
using System.Net.Sockets;
using DaxDiff.Domain;
using Microsoft.Identity.Client;

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

        if (ex is MsalException msal)
        {
            return msal.ErrorCode == MsalError.AuthenticationCanceledError
                ? new DaxDiffException(ErrorCategory.Authentication, "AUTH-CANCELLED", "Sign-in was cancelled.",
                    "Sign in again to continue.", inner: ex)
                : new DaxDiffException(ErrorCategory.Authentication, "AUTH-" + msal.ErrorCode.ToUpperInvariant(),
                    $"Sign-in failed: {msal.Message}",
                    "Check the tenant and client ID in Settings, then sign in again. Your administrator may need to grant consent.", inner: ex);
        }

        if (ex is TimeoutException || lower.Contains("timed out") || lower.Contains("timeout expired"))
            return new DaxDiffException(ErrorCategory.Timeout, "QRY-TIMEOUT", $"{transport}: the query timed out.",
                "Increase the timeout, add filters or reduce the grain.", isTransient: true, inner: ex);

        if (lower.Contains("xmla") && (lower.Contains("disabled") || lower.Contains("not enabled") || lower.Contains("not supported") || lower.Contains("read only")))
            return new DaxDiffException(ErrorCategory.UnsupportedModel, "CON-XMLA-DISABLED",
                "The XMLA endpoint is not available for this workspace.",
                "Ask a Fabric/Power BI administrator to enable XMLA read on the capacity and confirm the workspace is on Premium, PPU or Fabric capacity, or use the REST transport.", inner: ex);

        if (lower.Contains("premium") && lower.Contains("capacity"))
            return new DaxDiffException(ErrorCategory.UnsupportedModel, "CON-CAPACITY",
                "This workspace does not support XMLA connections (no Premium, PPU or Fabric capacity).",
                "Use the REST transport for this model or move the workspace to a supported capacity.", inner: ex);

        // The service gives the same answer for a wrong name, a workspace the account cannot see and one without capacity.
        if (lower.Contains("workspace") && lower.Contains("not found"))
            return new DaxDiffException(ErrorCategory.Configuration, "CON-WORKSPACE-NOT-FOUND",
                $"{transport}: the workspace was not found for the signed-in account.",
                "Paste the workspace connection link (Power BI: Workspace settings → License info → Connection link) into the workspace field. " +
                "Check that the tenant is the workspace's tenant, and that the account is a member of the workspace " +
                "(without a client ID, XMLA uses your Windows account unless you enter one in the Account field), " +
                "and that the workspace is on Premium, PPU or Fabric capacity (XMLA needs one).", inner: ex);

        if (lower.Contains("permission") || lower.Contains("not authorized") || lower.Contains("unauthorized") || lower.Contains("access is denied") || lower.Contains("forbidden"))
            return new DaxDiffException(ErrorCategory.Authorization, "CON-PERMISSION",
                "Access was denied for this model.",
                "Ask the model owner for Build (or Read) permission on the semantic model and access to the workspace.", inner: ex);

        if (lower.Contains("aadsts") || (lower.Contains("token") && lower.Contains("expired")))
            return new DaxDiffException(ErrorCategory.Authentication, "AUTH-EXPIRED", "Your sign-in has expired or is invalid.",
                "Sign out and sign in again.", inner: ex);

        if (ex is SocketException || ex is HttpRequestException { StatusCode: null } ||
            lower.Contains("no connection could be made") || lower.Contains("a connection cannot be made") ||
            lower.Contains("cannot connect") || lower.Contains("network"))
            return new DaxDiffException(ErrorCategory.Network, "CON-NETWORK", $"{transport}: could not reach the server.",
                "Check that Power BI Desktop is still running (local models) or your network/VPN connection (Service models), then retry.",
                isTransient: true, inner: ex);

        if (lower.Contains("query (") || lower.Contains("syntax") || lower.Contains("cannot be found") || lower.Contains("cannot find") ||
            lower.Contains("dax") || lower.Contains("failed to resolve"))
            return new DaxDiffException(ErrorCategory.InvalidQuery, "QRY-INVALID", $"The model rejected the query: {Trim(message)}",
                "Refresh metadata; a mapped table, column or measure may have been renamed or removed.", inner: ex);

        return new DaxDiffException(ErrorCategory.Unknown, "ERR-UNEXPECTED", $"{transport}: {Trim(message)}",
            "Retry. If the problem persists, export diagnostics from Settings and contact support.", inner: ex);
    }

    public static DaxDiffException FromHttpStatus(HttpStatusCode status, string body)
    {
        var detail = Trim(body);
        return status switch
        {
            HttpStatusCode.Unauthorized => new DaxDiffException(ErrorCategory.Authentication, "REST-401",
                "The Power BI service rejected your sign-in.", "Sign out and sign in again."),
            HttpStatusCode.Forbidden => new DaxDiffException(ErrorCategory.Authorization, "REST-403",
                "Access denied by the Power BI service.",
                "You need Build permission on the semantic model, and the tenant setting 'Dataset Execute Queries REST API' must be enabled."),
            HttpStatusCode.NotFound => new DaxDiffException(ErrorCategory.Configuration, "REST-404",
                "The workspace or semantic model was not found.", "Refresh the workspace list and reselect the model."),
            HttpStatusCode.TooManyRequests => new DaxDiffException(ErrorCategory.Throttled, "REST-429",
                "The Power BI service is throttling requests.", "Wait a minute and retry.", isTransient: true),
            HttpStatusCode.BadRequest => new DaxDiffException(ErrorCategory.InvalidQuery, "REST-400",
                $"The service rejected the query: {detail}", "Refresh metadata and check mapped fields."),
            >= HttpStatusCode.InternalServerError => new DaxDiffException(ErrorCategory.Network, $"REST-{(int)status}",
                "The Power BI service returned a server error.", "Retry later.", isTransient: true),
            _ => new DaxDiffException(ErrorCategory.Unknown, $"REST-{(int)status}", $"Unexpected service response: {detail}", "Retry."),
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
