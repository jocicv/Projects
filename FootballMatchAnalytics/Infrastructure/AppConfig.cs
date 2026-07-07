using System;
using System.IO;
using System.Text.Json;

namespace FootballMatchAnalytics.Infrastructure;

public sealed class AppConfig
{
    public string BaseUrl { get; init; } = "https://v3.football.api-sports.io";
    public string ApiKey { get; init; } = "";
    public int PollIntervalSeconds { get; init; } = 30;
    public string WebPrefix { get; init; } = "http://localhost:8080/";

    public static AppConfig Load()
    {

        string baseUrl = "https://v3.football.api-sports.io";
        string apiKey = "";
        int poll = 30;
        string prefix = "http://localhost:8080/";

        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(path))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("ApiFootball", out JsonElement apiSection))
                {
                    if (apiSection.TryGetProperty("BaseUrl", out JsonElement b) && b.ValueKind == JsonValueKind.String)
                        baseUrl = b.GetString() ?? baseUrl;
                    if (apiSection.TryGetProperty("ApiKey", out JsonElement k) && k.ValueKind == JsonValueKind.String)
                        apiKey = k.GetString() ?? apiKey;
                    if (apiSection.TryGetProperty("PollIntervalSeconds", out JsonElement p) && p.ValueKind == JsonValueKind.Number)
                        poll = p.GetInt32();
                }

                if (root.TryGetProperty("WebServer", out JsonElement webSection) &&
                    webSection.TryGetProperty("Prefix", out JsonElement pref) && pref.ValueKind == JsonValueKind.String)
                {
                    prefix = pref.GetString() ?? prefix;
                }
            }
            catch
            {

            }
        }

        apiKey = Environment.GetEnvironmentVariable("APIFOOTBALL_KEY") ?? apiKey;
        baseUrl = Environment.GetEnvironmentVariable("APIFOOTBALL_BASEURL") ?? baseUrl;
        prefix = Environment.GetEnvironmentVariable("WEB_PREFIX") ?? prefix;

        string? pollEnv = Environment.GetEnvironmentVariable("POLL_SECONDS");
        if (!string.IsNullOrWhiteSpace(pollEnv) && int.TryParse(pollEnv, out int pollParsed) && pollParsed > 0)
            poll = pollParsed;

        return new AppConfig
        {
            BaseUrl = baseUrl.TrimEnd('/'),
            ApiKey = apiKey,
            PollIntervalSeconds = poll,
            WebPrefix = prefix
        };
    }
}
