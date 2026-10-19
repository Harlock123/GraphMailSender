using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace GraphMailSender.Services;

/// <summary>Everything the app registration needs to acquire an app-only Graph token.</summary>
public sealed record GraphCredentials(string TenantId, string ClientId, string ClientSecret, string SenderAddress);

/// <summary>The message to hand to Graph's sendMail action.</summary>
public sealed record MailRequest(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string Subject,
    string Body,
    bool IsHtml,
    bool SaveToSentItems);

/// <summary>Raised when Graph (or the token endpoint) says no. Message is already user-readable.</summary>
public sealed class GraphMailException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Sends mail through the Graph REST endpoint POST /users/{sender}/sendMail using an
/// app-only (client credentials) token. Deliberately HttpClient + JSON rather than the
/// Microsoft.Graph SDK: one small dependency instead of the whole Kiota stack.
/// </summary>
public sealed class GraphMailService
{
    private const string GraphScope = "https://graph.microsoft.com/.default";
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    // Azure.Identity caches tokens per credential instance, so reuse the instance
    // while the entered credentials stay the same.
    private GraphCredentials? _cachedFor;
    private ClientSecretCredential? _credential;

    public GraphMailService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task SendAsync(GraphCredentials credentials, MailRequest mail, CancellationToken ct = default)
    {
        var token = await GetTokenAsync(credentials, ct).ConfigureAwait(false);

        // A dictionary rather than an anonymous type so any unused recipient list can be
        // left out of the JSON entirely instead of being sent as an empty array. Graph
        // needs at least one recipient across to/cc/bcc; the view model enforces that.
        var message = new Dictionary<string, object>
        {
            ["subject"] = mail.Subject,
            ["body"] = new
            {
                contentType = mail.IsHtml ? "HTML" : "Text",
                content = mail.Body,
            },
        };

        if (mail.To.Count > 0)
        {
            message["toRecipients"] = ToRecipients(mail.To);
        }

        if (mail.Cc.Count > 0)
        {
            message["ccRecipients"] = ToRecipients(mail.Cc);
        }

        if (mail.Bcc.Count > 0)
        {
            message["bccRecipients"] = ToRecipients(mail.Bcc);
        }

        var payload = new
        {
            message,
            saveToSentItems = mail.SaveToSentItems,
        };

        var url = $"{GraphBaseUrl}/users/{Uri.EscapeDataString(credentials.SenderAddress)}/sendMail";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new GraphMailException($"Could not reach Microsoft Graph: {ex.Message}", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new GraphMailException(
                $"Graph returned {(int)response.StatusCode} {response.ReasonPhrase}: {DescribeGraphError(body)}");
        }
    }

    private async Task<string> GetTokenAsync(GraphCredentials credentials, CancellationToken ct)
    {
        if (_credential is null || _cachedFor != credentials)
        {
            _credential = new ClientSecretCredential(
                credentials.TenantId,
                credentials.ClientId,
                credentials.ClientSecret);
            _cachedFor = credentials;
        }

        try
        {
            var token = await _credential
                .GetTokenAsync(new TokenRequestContext([GraphScope]), ct)
                .ConfigureAwait(false);
            return token.Token;
        }
        catch (Azure.Identity.AuthenticationFailedException ex)
        {
            // A bad secret/client id means the cached credential is useless.
            _credential = null;
            _cachedFor = null;
            throw new GraphMailException($"Sign-in failed: {Flatten(ex)}", ex);
        }
    }

    private static object[] ToRecipients(IReadOnlyList<string> addresses) =>
        addresses.Select(address => new { emailAddress = new { address } }).ToArray();

    /// <summary>
    /// Joins an exception chain into one line. Azure.Identity's own message is just
    /// "ClientSecretCredential authentication failed:" with nothing after it - the AADSTS
    /// code that actually says what went wrong is down in the inner MsalServiceException.
    /// </summary>
    private static string Flatten(Exception exception)
    {
        var parts = new List<string>();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message?.Trim().TrimEnd(':').Trim();
            if (!string.IsNullOrEmpty(message) && !parts.Contains(message))
            {
                parts.Add(message);
            }
        }

        return parts.Count > 0 ? string.Join(" - ", parts) : exception.GetType().Name;
    }

    /// <summary>Pull the human-readable bit out of a Graph error envelope, falling back to the raw body.</summary>
    private static string DescribeGraphError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(no response body)";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var c) ? c.GetString() : null;
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return string.IsNullOrWhiteSpace(code) ? message! : $"{code} - {message}";
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to the raw body.
        }

        return body.Length > 500 ? body[..500] + "..." : body;
    }
}
