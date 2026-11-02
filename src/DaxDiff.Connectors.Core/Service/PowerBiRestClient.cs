using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DaxDiff.Connectors.Auth;
using Microsoft.Extensions.Logging;

namespace DaxDiff.Connectors.Service;

/// <summary>Authenticated Power BI REST calls with 429/5xx backoff that honours Retry-After (§8).</summary>
public sealed class PowerBiRestClient
{
    private readonly HttpClient _http;
    private readonly IAuthService _auth;
    private readonly AuthOptions _options;
    private readonly ILogger<PowerBiRestClient> _logger;

    public PowerBiRestClient(HttpClient http, IAuthService auth, AuthOptions options, ILogger<PowerBiRestClient> logger)
    {
        _http = http;
        _auth = auth;
        _options = options;
        _logger = logger;
    }

    public int MaxAttempts { get; init; } = 4;

    public Task<JsonDocument> GetAsync(string relativePath, CancellationToken cancellationToken) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url(relativePath)), cancellationToken);

    public Task<JsonDocument> PostJsonAsync(string relativePath, string json, CancellationToken cancellationToken) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Post, Url(relativePath))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }, cancellationToken);

    private string Url(string relativePath) => _options.PowerBiApiBase.TrimEnd('/') + "/" + relativePath.TrimStart('/');

    private async Task<JsonDocument> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var token = await _auth.GetTokenAsync(cancellationToken);
            using var request = createRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Power BI REST network failure, attempt {Attempt}", attempt);
                await Task.Delay(Backoff(attempt, null), cancellationToken);
                continue;
            }
            catch (HttpRequestException ex)
            {
                throw ErrorClassifier.Classify(ex, "Power BI REST");
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                }

                var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (transient && attempt < MaxAttempts)
                {
                    var delay = Backoff(attempt, response.Headers.RetryAfter);
                    _logger.LogWarning("Power BI REST returned {Status}; retrying in {Delay}", (int)response.StatusCode, delay);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw ErrorClassifier.FromHttpStatus(response.StatusCode, ExtractError(body));
            }
        }
    }

    private static TimeSpan Backoff(int attempt, RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
            return delta;
        if (retryAfter?.Date is { } date && date > DateTimeOffset.UtcNow)
            return date - DateTimeOffset.UtcNow;
        return TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
    }

    private static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var c) ? c.GetString() : null;
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                var details = error.TryGetProperty("pbi.error", out var pbi) ? pbi.ToString() : null;
                return string.Join(" ", new[] { code, message, details }.Where(s => !string.IsNullOrEmpty(s)));
            }
        }
        catch (JsonException)
        {
        }

        return body;
    }
}
