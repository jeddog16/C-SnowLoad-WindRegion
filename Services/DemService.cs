using System.Globalization;
using System.Text.Json;
using AhdApi.Models;
using Microsoft.Extensions.Options;

namespace AhdApi.Services;

public class DemService
{
    private static readonly string[] PreferredElevationFieldNames =
    {
        "value",
        "pixel value",
        "pixel_value",
        "elevation",
        "elev",
        "z",
        "gridcode",
        "height",
        "ahd"
    };

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
        var attempts = new List<Dictionary<string, object>>();

        // 1) Try GA LiDAR first
        var lidarAttempt = new Dictionary<string, object>
        {
            ["source"] = "ga-lidar",
            ["upstream"] = _settings.GaLidarIdentifyUrl
        };

        try
        {
            double? lidarElevation = await SampleGaLidarAsync(lat, lon, lidarAttempt);

            if (lidarElevation.HasValue)
            {
                lidarAttempt["success"] = true;
                lidarAttempt["elevation"] = lidarElevation.Value;
                attempts.Add(lidarAttempt);

                var resp = new AhdResponse
                {
                    CodeVersion = "csharp-port-1.0.0",
                    Lat = lat,
                    Lon = lon,
                    AhdM = lidarElevation.Value,
                    Method = "dem",
                    VerticalDatum = "AHD",
                    Source = "ga-lidar",
                    SourceType = "dem",
                    Upstream = _settings.GaLidarIdentifyUrl
                };

                _regionService.AddRegionInfo(resp);

                if (debug)
                {
                    resp.Extra = new Dictionary<string, object>
                    {
                        ["attempts"] = attempts
                    };
                }

                return resp;
            }

            lidarAttempt["success"] = false;
            lidarAttempt["reason"] = "No elevation returned.";
            attempts.Add(lidarAttempt);
        }
        catch (Exception ex)
        {
            lidarAttempt["success"] = false;
            lidarAttempt["reason"] = ex.Message;
            attempts.Add(lidarAttempt);
        }

        // 2) If lidar is required, stop here
        if (requireLidar)
        {
            throw new Exception(JsonSerializer.Serialize(new
            {
                error = "Lidar was required, but no LiDAR source returned an elevation.",
                attempts
            }));
        }

        // 3) Fall back to GA SRTM
        var srtmAttempt = new Dictionary<string, object>
        {
            ["source"] = "ga-srtm",
            ["upstream"] = _settings.GaSrtmIdentifyUrl
        };

        try
        {
            double? srtmElevation = await SampleArcGisIdentifyAsync(_settings.GaSrtmIdentifyUrl, lat, lon);

            if (srtmElevation.HasValue)
            {
                srtmAttempt["success"] = true;
                srtmAttempt["elevation"] = srtmElevation.Value;
                attempts.Add(srtmAttempt);

                var resp = new AhdResponse
                {
                    CodeVersion = "csharp-port-1.0.0",
                    Lat = lat,
                    Lon = lon,
                    AhdM = srtmElevation.Value,
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
                        ["attempts"] = attempts
                    };
                }

                return resp;
            }

            srtmAttempt["success"] = false;
            srtmAttempt["reason"] = "No elevation returned.";
            attempts.Add(srtmAttempt);
        }
        catch (Exception ex)
        {
            srtmAttempt["success"] = false;
            srtmAttempt["reason"] = ex.Message;
            attempts.Add(srtmAttempt);
        }

        throw new Exception(JsonSerializer.Serialize(new
        {
            error = "No DEM source returned an elevation.",
            attempts
        }));
    }

    private async Task<double?> SampleGaLidarAsync(double lat, double lon, Dictionary<string, object> lidarAttempt)
    {
        var probes = new List<Dictionary<string, object>>();
        lidarAttempt["identify_probes"] = probes;

        // ArcGIS MapServer identify behaviour can vary by layer visibility and render context.
        // Probe a few request shapes before giving up and falling back to SRTM.
        var probeConfigs = new[]
        {
            new { Layers = "all", Tolerance = 1, Delta = 0.010, ImageDisplay = "400,400,96" },
            new { Layers = "top", Tolerance = 2, Delta = 0.005, ImageDisplay = "1024,1024,96" },
            new { Layers = "all", Tolerance = 4, Delta = 0.002, ImageDisplay = "2048,2048,96" }
        };

        foreach (var config in probeConfigs)
        {
            var probe = new Dictionary<string, object>
            {
                ["layers"] = config.Layers,
                ["tolerance"] = config.Tolerance,
                ["delta"] = config.Delta,
                ["imageDisplay"] = config.ImageDisplay
            };

            try
            {
                double? elevation = await SampleArcGisIdentifyAsync(
                    _settings.GaLidarIdentifyUrl,
                    lat,
                    lon,
                    config.Layers,
                    config.Tolerance,
                    config.Delta,
                    config.ImageDisplay);

                if (elevation.HasValue)
                {
                    probe["success"] = true;
                    probe["elevation"] = elevation.Value;
                    probes.Add(probe);
                    return elevation;
                }

                probe["success"] = false;
                probe["reason"] = "No elevation returned.";
                probes.Add(probe);
            }
            catch (Exception ex)
            {
                probe["success"] = false;
                probe["reason"] = ex.Message;
                probes.Add(probe);
            }
        }

        return null;
    }

    private async Task<double?> SampleArcGisIdentifyAsync(
        string identifyUrl,
        double lat,
        double lon,
        string layers = "all",
        int tolerance = 1,
        double delta = 0.01,
        string imageDisplay = "400,400,96")
    {
        string geometry = JsonSerializer.Serialize(new
        {
            x = lon,
            y = lat,
            spatialReference = new { wkid = 4326 }
        });

        string mapExtent = string.Join(",",
            (lon - delta).ToString(CultureInfo.InvariantCulture),
            (lat - delta).ToString(CultureInfo.InvariantCulture),
            (lon + delta).ToString(CultureInfo.InvariantCulture),
            (lat + delta).ToString(CultureInfo.InvariantCulture));

        string url =
            $"{identifyUrl}" +
            $"?f=json" +
            $"&geometry={Uri.EscapeDataString(geometry)}" +
            $"&geometryType=esriGeometryPoint" +
            $"&sr=4326" +
            $"&layers={Uri.EscapeDataString(layers)}" +
            $"&tolerance={tolerance.ToString(CultureInfo.InvariantCulture)}" +
            $"&mapExtent={Uri.EscapeDataString(mapExtent)}" +
            $"&imageDisplay={Uri.EscapeDataString(imageDisplay)}" +
            $"&returnGeometry=false";

        using var response = await _httpClient.GetAsync(url);
        string json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Identify request failed. Status={(int)response.StatusCode}. Body={json}");
        }

        using JsonDocument doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("error", out JsonElement error))
        {
            throw new Exception($"ArcGIS service returned error: {error}");
        }

        if (!doc.RootElement.TryGetProperty("results", out JsonElement results))
        {
            throw new Exception($"Response did not contain 'results'. Raw JSON: {json}");
        }

        foreach (JsonElement result in results.EnumerateArray())
        {
            if (result.TryGetProperty("value", out JsonElement valueElement))
            {
                if (TryParseNumericElement(valueElement, out double value))
                    return value;
            }

            if (result.TryGetProperty("attributes", out JsonElement attributes))
            {
                foreach (string preferredName in PreferredElevationFieldNames)
                {
                    foreach (JsonProperty prop in attributes.EnumerateObject())
                    {
                        if (!prop.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (TryParseNumericElement(prop.Value, out double preferredValue))
                            return preferredValue;
                    }
                }

                foreach (JsonProperty prop in attributes.EnumerateObject())
                {
                    if (IsLikelyIdentifierField(prop.Name))
                        continue;

                    if (TryParseNumericElement(prop.Value, out double attrValue))
                        return attrValue;
                }
            }
        }

        return null;
    }

    private static bool TryParseNumericElement(JsonElement element, out double value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out value);

            case JsonValueKind.String:
                return double.TryParse(
                    element.GetString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out value);

            default:
                value = default;
                return false;
        }
    }

    private static bool IsLikelyIdentifierField(string fieldName)
    {
        string lowered = fieldName.ToLowerInvariant();

        return lowered.Contains("objectid")
            || lowered.Equals("id")
            || lowered.EndsWith("_id")
            || lowered.Equals("fid")
            || lowered.Equals("oid")
            || lowered.Contains("shape_length")
            || lowered.Contains("shape_area")
            || lowered.Contains("class");
    }
}
