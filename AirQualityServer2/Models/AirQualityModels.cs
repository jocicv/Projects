using System.Text.Json.Serialization;

namespace AirQualityServer.Models
{
    public class IQAirResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public CityData? Data { get; set; }
    }

    public class CityData
    {
        [JsonPropertyName("city")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("country")]
        public string Country { get; set; } = string.Empty;

        [JsonPropertyName("location")]
        public Location? Location { get; set; }

        [JsonPropertyName("current")]
        public CurrentData? Current { get; set; }
    }

    public class Location
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("coordinates")]
        public double[]? Coordinates { get; set; }
    }

    public class CurrentData
    {
        [JsonPropertyName("pollution")]
        public Pollution? Pollution { get; set; }

        [JsonPropertyName("weather")]
        public Weather? Weather { get; set; }
    }

    public class Pollution
    {
        [JsonPropertyName("ts")]
        public string Timestamp { get; set; } = string.Empty;

        [JsonPropertyName("aqius")]
        public int AqiUs { get; set; }

        [JsonPropertyName("mainus")]
        public string MainPollutantUs { get; set; } = string.Empty;

        [JsonPropertyName("aqicn")]
        public int AqiCn { get; set; }

        [JsonPropertyName("maincn")]
        public string MainPollutantCn { get; set; } = string.Empty;
    }

    public class Weather
    {
        [JsonPropertyName("ts")]
        public string Timestamp { get; set; } = string.Empty;

        [JsonPropertyName("tp")]
        public double Temperature { get; set; }

        [JsonPropertyName("pr")]
        public int Pressure { get; set; }

        [JsonPropertyName("hu")]
        public int Humidity { get; set; }

        [JsonPropertyName("ws")]
        public double WindSpeed { get; set; }

        [JsonPropertyName("wd")]
        public int WindDirection { get; set; }

        [JsonPropertyName("ic")]
        public string WeatherIcon { get; set; } = string.Empty;
    }

    public class ClientRequest
    {
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    }

    public enum ResultOrigin
    {
        Api,
        Cache,
        Coalesced
    }

    public class ClientResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public CityData? Data { get; set; }
        public ResultOrigin Origin { get; set; } = ResultOrigin.Api;
        public bool FromCache { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
}
