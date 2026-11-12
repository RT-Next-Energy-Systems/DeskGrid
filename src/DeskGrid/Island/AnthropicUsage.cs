using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeskGrid.Diagnostics;

namespace DeskGrid.Island;

/// <summary>
/// The plan usage Anthropic itself reports for the account Claude Code is signed in to (the 5-hour and the weekly window, as
/// the usage page shows them). It is the only source of Claude's own percentage when the status line gives none. Opt-in
/// (<c>usageOnline</c>, off by default): it reads the sign-in token Claude Code keeps in <c>~/.claude/.credentials.json</c> and
/// asks <c>api.anthropic.com</c> for the usage. The token is used for that one request and never logged, stored or shown;
/// when it has expired nothing is asked (Claude Code renews it itself when it is used).
/// </summary>
internal static class AnthropicUsage
{
    public const string Endpoint = "https://api.anthropic.com/api/oauth/usage";

    public static string CredentialsFile(string home) => Path.Combine(home, ".claude", ".credentials.json");

    /// <summary>The access token in the credentials file, or <see langword="null"/> (no file, no sign-in, or the token has expired).</summary>
    public static string? ReadToken(string file, DateTimeOffset now)
    {
        try
        {
            return File.Exists(file) ? TokenOf(File.ReadAllText(file, Encoding.UTF8), now) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string? TokenOf(string json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object
                || !oauth.TryGetProperty("accessToken", out var token) || token.ValueKind != JsonValueKind.String
                || token.GetString() is not { Length: > 0 } text)
            {
                return null;
            }

            // expiresAt is Unix milliseconds; 0 or missing means unknown, which is tried.
            if (oauth.TryGetProperty("expiresAt", out var expires) && expires.ValueKind == JsonValueKind.Number
                && expires.TryGetInt64(out var at) && at > 0 && DateTimeOffset.FromUnixTimeMilliseconds(at) <= now)
            {
                return null;
            }

            return text;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Reads the answer of the usage endpoint; <see langword="null"/> when it holds no running 5-hour window.</summary>
    internal static PlanUsage? Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (Window(root, "five_hour", now) is not { } five)
            {
                return null;
            }

            var week = Window(root, "seven_day", now);
            return new PlanUsage(five.Percent, five.Resets, week?.Percent, week?.Resets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (double Percent, DateTimeOffset Resets)? Window(JsonElement root, string name, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
            || !window.TryGetProperty("utilization", out var used) || used.ValueKind != JsonValueKind.Number || !used.TryGetDouble(out var percent)
            || !window.TryGetProperty("resets_at", out var resets) || resets.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(resets.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var end))
        {
            return null;
        }

        return end > now && double.IsFinite(percent) ? (Math.Clamp(percent, 0, 100), end) : null;
    }

    /// <summary>
    /// Asks the endpoint for the usage. <see langword="null"/> when it answers with an error or something unreadable; the
    /// status code is logged, nothing else of the exchange is.
    /// </summary>
    public static async Task<PlanUsage?> FetchAsync(HttpClient client, string token, DateTimeOffset now, CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await client.SendAsync(request, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Info($"The plan usage could not be read from Anthropic: HTTP {(int)response.StatusCode}.");
                return null;
            }

            return Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false), now);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Info($"The plan usage could not be read from Anthropic: {exception.GetType().Name}.");
            return null;
        }
    }
}

/// <summary>
/// Asks <see cref="AnthropicUsage"/> no more often than needed: every few minutes, longer after a failure, and between asks the
/// last answer stands while its window is running. Used from one thread at a time.
/// </summary>
internal sealed class OnlineUsage(string credentialsFile, Func<HttpClient> client)
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(15);

    private PlanUsage? _last;
    private DateTimeOffset _lastAt;
    private DateTimeOffset _next = DateTimeOffset.MinValue;
    private bool _toldNoSignIn;

    public async Task<PlanUsage?> ReadAsync(DateTimeOffset now)
    {
        if (now >= _next)
        {
            var token = AnthropicUsage.ReadToken(credentialsFile, now);
            if (token is null)
            {
                _next = now + Every;
                if (!_toldNoSignIn)
                {
                    _toldNoSignIn = true;
                    Log.Info("Plan usage from Anthropic is on, but Claude Code has no usable sign-in on this computer.");
                }
            }
            else if (await AnthropicUsage.FetchAsync(client(), token, now).ConfigureAwait(false) is { } fresh)
            {
                (_last, _lastAt, _next, _toldNoSignIn) = (fresh, now, now + Every, false);
            }
            else
            {
                _next = now + AfterFailure;
            }
        }

        return _last is { } plan && now - _lastAt < Stale && plan.FiveResets > now ? plan : null;
    }
}
