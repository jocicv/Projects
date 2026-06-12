using System.Text.Json;
using AirQualityServer.Logging;
using AirQualityServer.Models;

namespace AirQualityServer.Workers
{

    public sealed class IQAirApiClient
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static readonly ThreadSafeLogger Logger = ThreadSafeLogger.Instance;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string _apiKey;
        private const string BaseUrl = "http://api.airvisual.com/v2/city";

        public IQAirApiClient(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("IQAir API kljuc ne sme biti prazan.", nameof(apiKey));
            _apiKey = apiKey;
        }

        public async Task<CityData> FetchCityDataAsync(string city, string state, string country,
                                                       string requestId, CancellationToken ct = default)
        {
            var url = $"{BaseUrl}?city={Uri.EscapeDataString(city)}" +
                      $"&state={Uri.EscapeDataString(state)}" +
                      $"&country={Uri.EscapeDataString(country)}" +
                      $"&key={_apiKey}";

            Logger.Info($"Poziv IQAir API-ja: city={city}, state={state}, country={country}", requestId);

            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url, ct);
            }
            catch (TaskCanceledException)
            {
                throw new Exception("IQAir API zahtev nije zavrsen na vreme (10s).");
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Mrezna greska pri kontaktiranju IQAir API-ja: {ex.Message}");
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning($"IQAir API vratio HTTP {(int)response.StatusCode}: {body}", requestId);
                throw new Exception($"IQAir API greska (HTTP {(int)response.StatusCode}). Odgovor: {body}");
            }

            IQAirResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<IQAirResponse>(body, _jsonOptions);
            }
            catch (JsonException ex)
            {
                throw new Exception($"Greska pri parsiranju odgovora IQAir API-ja: {ex.Message}");
            }

            if (parsed == null || parsed.Status != "success" || parsed.Data == null)
            {
                var status = parsed?.Status ?? "nepoznat";
                throw new Exception($"IQAir API vratio status '{status}' — nema podataka za {city}, {state}, {country}.");
            }

            Logger.Info($"IQAir API uspeh: AQI={parsed.Data.Current?.Pollution?.AqiUs}", requestId);
            return parsed.Data;
        }
    }
}
