using System.Globalization;
using System.Text.Json;
using AhdApi.Models;
using Microsoft.Extensions.Options;

namespace AhdApi.Services;

public class DemService
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly RegionService _regionService;

    public DemService(
        HttpClient httpClient,
        IOptions<AppSettings> options,
        RegionService regionService)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _regionService = regionService;
    }

    public async Task<AhdResponse> SampleBestDemAsync(double lat, double lon, bool debug, bool requireLidar)
    {
        double? elevation = await SampleGaSrtmAsync(lat, lon);

        if (elevation == null)
            throw new Exception("Could not get DEM elevation from GA SRTM.");

        var resp = new AhdResponse
        {
            CodeVersion = "csharp-port-1.0.0",
            Lat = lat,
            Lon = lon,
            AhdM = elevation,
            Method = "dem",
            VerticalDatum = "AHD",
            Source = "ga-srtm",
            SourceType = "dem",
            Upstream = _settings.GaSrtmIdentifyUrl
        };

        _regionService.AddRegionInfo(resp);

        if (debug)
        {
            resp.Extra = new Dictionary<string, object>
            {
                ["debug"] = "Using GA SRTM only in first implementation."
            };
        }

        return resp;
    }

    private async Task<double?> SampleGaSrtmAsync(double lat, double lon)
    {
        string geometry = JsonSerializer.Serialize(new
        {
            x = lon,
            y = lat,
            spatialReference = new { wkid = 4326 }
        });

        double delta = 0.01;
        string mapExtent = string.Join(",",
            (lon - delta).ToString(CultureInfo.InvariantCulture),
            (lat - delta).ToString(CultureInfo.InvariantCulture),
            (lon + delta).ToString(CultureInfo.InvariantCulture),
            (lat + delta).ToString(CultureInfo.InvariantCulture));

        string url =
            $"{_settings.GaSrtmIdentifyUrl}" +
            $"?f=json" +
            $"&geometry={Uri.EscapeDataString(geometry)}" +
            $"&geometryType=esriGeometryPoint" +
            $"&sr=4326" +
            $"&layers=all" +
            $"&tolerance=1" +
            $"&mapExtent={Uri.EscapeDataString(mapExtent)}" +
            $"&imageDisplay=400,400,96" +
            $"&returnGeometry=false";

        using var response = await _httpClient.GetAsync(url);
        string json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"GA SRTM request failed. Status={(int)response.StatusCode}. Body={json}");
        }

        using JsonDocument doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("results", out JsonElement results))
            throw new Exception($"GA SRTM response did not contain 'results'. Raw JSON: {json}");

        foreach (JsonElement result in results.EnumerateArray())
        {
            if (result.TryGetProperty("value", out JsonElement valueElement))
            {
                string? valueString = valueElement.GetString();

                if (double.TryParse(valueString, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                    return value;
            }

            if (result.TryGetProperty("attributes", out JsonElement attributes))
            {
                foreach (JsonProperty prop in attributes.EnumerateObject())
                {
                    string raw = prop.Value.ToString();

                    if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double attrValue))
                        return attrValue;
                }
            }
        }

        throw new Exception($"Could not parse elevation from GA SRTM response. Raw JSON: {json}");
    }
}