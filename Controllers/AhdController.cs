using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AhdApi.Models;
using AhdApi.Services;

namespace AhdApi.Controllers;

[ApiController]
[Route("")]
public class AhdController : ControllerBase
{
    private readonly DemService _demService;
    private readonly GnssService _gnssService;
    private readonly NmeaService _nmeaService;
    private readonly WindRegionService _windRegionService;
    private readonly SnowRegionService _snowRegionService;

    public AhdController(
        DemService demService,
        GnssService gnssService,
        NmeaService nmeaService,
        WindRegionService windRegionService,
        SnowRegionService snowRegionService)
    {
        _demService = demService;
        _gnssService = gnssService;
        _nmeaService = nmeaService;
        _windRegionService = windRegionService;
        _snowRegionService = snowRegionService;
    }

    [HttpGet("")]
    public IActionResult Root()
    {
        return Ok(new
        {
            name = "AHD Elevation API",
            version = "csharp-port-1.0.0",
            endpoints = new[]
            {
                "/health",
                "/ahd",
                "/ahd_bulk_csv",
                "/ahd_gnss",
                "/ahd_from_nmea_gga",
                "/wind_debug"
            }
        });
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "ok"
        });
    }

    [HttpGet("ahd")]
    public async Task<IActionResult> Ahd(
        [FromQuery] double lat,
        [FromQuery] double lon,
        [FromQuery] bool debug = false,
        [FromQuery] bool require_lidar = false)
    {
        try
        {
            var result = await _demService.SampleBestDemAsync(lat, lon, debug, require_lidar);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = ex.Message,
                detail = ex.ToString()
            });
        }
    }

    [HttpGet("ahd_gnss")]
    public IActionResult AhdGnss(
        [FromQuery] double lat,
        [FromQuery] double lon,
        [FromQuery(Name = "h_ellipsoid_m")] double hEllipsoidM)
    {
        try
        {
            var result = _gnssService.GnssToAhd(lat, lon, hEllipsoidM);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = ex.Message,
                detail = ex.ToString()
            });
        }
    }

    [HttpPost("ahd_from_nmea_gga")]
    public IActionResult AhdFromNmea([FromBody] NmeaRequest request)
    {
        try
        {
            var gga = _nmeaService.ParseGga(request.Nmea);

            var result = _gnssService.GnssToAhd(
                gga["lat"],
                gga["lon"],
                gga["h_ellipsoid_m"]);

            result.Extra = new Dictionary<string, object>
            {
                ["nmea_alt_above_geoid_m"] = gga["alt_above_geoid_m"],
                ["nmea_geoid_sep_m"] = gga["geoid_sep_m"]
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = ex.Message,
                detail = ex.ToString()
            });
        }
    }

    [HttpGet("wind_debug")]
    public IActionResult WindDebug()
    {
        try
        {
            return Ok(new
            {
                wind = _windRegionService.DebugInfo(),
                snow = _snowRegionService.DebugInfo()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = ex.Message,
                detail = ex.ToString()
            });
        }
    }

    [HttpPost("ahd_bulk_csv")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> AhdBulkCsv(
        IFormFile file,
        [FromQuery] bool debug = false,
        [FromQuery(Name = "require_lidar")] bool requireLidar = false)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "CSV file is required." });
        }

        string csvText;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            csvText = await reader.ReadToEndAsync();
        }

        var lines = csvText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count < 2)
        {
            return BadRequest(new { error = "CSV must include a header row and at least one data row." });
        }

        var header = ParseCsvLine(lines[0]);
        int latIndex = header.FindIndex(h => string.Equals(h?.Trim(), "lat", StringComparison.OrdinalIgnoreCase));
        int lonIndex = header.FindIndex(h => string.Equals(h?.Trim(), "lon", StringComparison.OrdinalIgnoreCase));

        if (latIndex < 0 || lonIndex < 0)
        {
            return BadRequest(new { error = "CSV header must contain 'lat' and 'lon' columns." });
        }

        var output = new StringBuilder();
        AppendCsvRow(
            output,
            "row",
            "lat",
            "lon",
            "code_version",
            "h_ellipsoid_m",
            "ahd_m",
            "n_ahd_m",
            "method",
            "vertical_datum",
            "source",
            "source_type",
            "upstream",
            "wind_region",
            "snow_region",
            "is_snow_load_region",
            "wind_region_error",
            "extra",
            "error");

        for (int i = 1; i < lines.Count; i++)
        {
            var cols = ParseCsvLine(lines[i]);
            int rowNumber = i + 1;

            string latRaw = latIndex < cols.Count ? cols[latIndex].Trim() : "";
            string lonRaw = lonIndex < cols.Count ? cols[lonIndex].Trim() : "";

            if (!double.TryParse(latRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) ||
                !double.TryParse(lonRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            {
                AppendCsvRow(
                    output,
                    rowNumber.ToString(CultureInfo.InvariantCulture),
                    latRaw,
                    lonRaw,
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "Invalid lat/lon");
                continue;
            }

            try
            {
                var result = await _demService.SampleBestDemAsync(lat, lon, debug, requireLidar);
                string extraJson = result.Extra == null ? "" : JsonSerializer.Serialize(result.Extra);

                AppendCsvRow(
                    output,
                    rowNumber.ToString(CultureInfo.InvariantCulture),
                    lat.ToString(CultureInfo.InvariantCulture),
                    lon.ToString(CultureInfo.InvariantCulture),
                    result.CodeVersion ?? "",
                    result.HEllipsoidM?.ToString(CultureInfo.InvariantCulture) ?? "",
                    result.AhdM?.ToString(CultureInfo.InvariantCulture) ?? "",
                    result.NAhdM?.ToString(CultureInfo.InvariantCulture) ?? "",
                    result.Method ?? "",
                    result.VerticalDatum ?? "",
                    result.Source ?? "",
                    result.SourceType ?? "",
                    result.Upstream ?? "",
                    result.WindRegion ?? "",
                    result.SnowRegion ?? "",
                    result.IsSnowLoadRegion.ToString().ToLowerInvariant(),
                    result.WindRegionError ?? "",
                    extraJson,
                    "");
            }
            catch (Exception ex)
            {
                AppendCsvRow(
                    output,
                    rowNumber.ToString(CultureInfo.InvariantCulture),
                    lat.ToString(CultureInfo.InvariantCulture),
                    lon.ToString(CultureInfo.InvariantCulture),
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    ex.Message);
            }
        }

        var bytes = Encoding.UTF8.GetBytes(output.ToString());
        return File(bytes, "text/csv", "ahd_bulk_results.csv");
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        values.Add(current.ToString());
        return values;
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        bool needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        string escaped = value.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{escaped}\"" : escaped;
    }

    private static void AppendCsvRow(StringBuilder output, params string?[] values)
    {
        output.AppendLine(string.Join(",", values.Select(CsvEscape)));
    }
}
