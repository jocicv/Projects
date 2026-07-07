using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FootballMatchAnalytics.Infrastructure;
using FootballMatchAnalytics.Models;

namespace FootballMatchAnalytics.Services;

public sealed record FixturesResult(bool Success, IReadOnlyList<FixtureItem> Fixtures, string? Error);

public sealed class FootballApiClient
{
    private readonly HttpClient _http;
    private readonly ConsoleLogger _log;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FootballApiClient(HttpClient http, ConsoleLogger log)
    {
        _http = http;
        _log = log;
    }

    public async Task<FixturesResult> GetFixturesAsync(int teamId, int season, CancellationToken ct)
    {
        string url = $"/fixtures?team={teamId}&season={season}";

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                string reason = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Nevažeći ili nedostajući API ključ (401).",
                    HttpStatusCode.Forbidden => "Pristup odbijen — proveri API ključ/plan (403).",
                    (HttpStatusCode)429 => "Prekoračen limit zahteva (429). Sačekaj pa pokušaj ponovo.",
                    _ => $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}."
                };
                return new FixturesResult(false, Array.Empty<FixtureItem>(), reason);
            }

            string body = await response.Content.ReadAsStringAsync(ct);
            FixturesResponse? parsed = JsonSerializer.Deserialize<FixturesResponse>(body, JsonOptions);

            if (parsed is null)
                return new FixturesResult(false, Array.Empty<FixtureItem>(), "Prazan ili neispravan odgovor API-ja.");

            if ((parsed.Response is null || parsed.Response.Count == 0) && parsed.HasErrors)
                return new FixturesResult(false, Array.Empty<FixtureItem>(), $"API greška: {parsed.Errors}");

            IReadOnlyList<FixtureItem> fixtures = parsed.Response ?? new List<FixtureItem>();
            return new FixturesResult(true, fixtures, null);
        }
        catch (OperationCanceledException)
        {

            return new FixturesResult(false, Array.Empty<FixtureItem>(), "Zahtev otkazan.");
        }
        catch (Exception ex)
        {
            _log.Error($"[API] Request failed {url}: {ex.Message}");
            return new FixturesResult(false, Array.Empty<FixtureItem>(), ex.Message);
        }
    }
}
