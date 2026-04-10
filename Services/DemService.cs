using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using AhdApi.Models;
using BitMiracle.LibTiff.Classic;
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

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TileDownloadLocks = new();

    private static readonly Dictionary<string, StateBounds> SnowStateBounds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NSW"] = new StateBounds("NSW", 141.0, 154.0, -37.6, -28.0),
        ["VIC"] = new StateBounds("VIC", 141.0, 150.1, -39.3, -33.8),
        ["TAS"] = new StateBounds("TAS", 143.5, 148.7, -43.9, -39.0)
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

    public async Task<AhdResponse> SampleBestDemAsync(double lat, double lon, bool debug, bool requireLidar, string? demModeOverride = null)
    {
        var attempts = new List<Dictionary<string, object>>();
        string demMode = (string.IsNullOrWhiteSpace(demModeOverride) ? _settings.DemLookupMode : demModeOverride)
            ?.Trim()
            .ToLowerInvariant() ?? "hybrid";
        bool gaSrtmOnlyMode = demMode is "ga_srtm_identify" or "ga_srtm_only" or "srtm_only";
        bool r2OnlyMode = demMode is "r2_only";

        if (gaSrtmOnlyMode && requireLidar)
        {
            throw new Exception("require_lidar=true is not compatible with DemLookupMode=ga_srtm_identify.");
        }

        // 1) Try cached/lazy R2 snow tiles first.
        if (!gaSrtmOnlyMode &&
            _settings.EnableR2TileFetch &&
            !string.IsNullOrWhiteSpace(_settings.R2TileBaseUrl))
        {
            var r2Attempt = new Dictionary<string, object>
            {
                ["source"] = "r2-tile-cache",
                ["upstream"] = _settings.R2TileBaseUrl
            };

            try
            {
                TileSampleResult? tileSample = await TrySampleR2TileAsync(lat, lon);
                if (tileSample != null)
                {
                    r2Attempt["elevation"] = tileSample.Elevation;
                    r2Attempt["tile"] = tileSample.TileId;
                    r2Attempt["state"] = tileSample.State;
                    r2Attempt["cache"] = tileSample.LocalPath;
                    r2Attempt["downloaded"] = tileSample.DownloadedNow;

                    if (IsNearZeroLidarElevation(tileSample.Elevation))
                    {
                        r2Attempt["success"] = false;
                        r2Attempt["reason"] = "LiDAR tile returned near-zero elevation; falling back to SRTM.";
                        attempts.Add(r2Attempt);
                    }
                    else
                    {
                        r2Attempt["success"] = true;
                        attempts.Add(r2Attempt);

                        var resp = new AhdResponse
                        {
                            CodeVersion = "csharp-port-1.0.0",
                            Lat = lat,
                            Lon = lon,
                            AhdM = tileSample.Elevation,
                            Method = "dem",
                            VerticalDatum = "AHD",
                            Source = "r2-tile-cache",
                            SourceType = "dem",
                            Upstream = _settings.R2TileBaseUrl,
                            VerticalAccuracy95M = 0.3,
                            VerticalAccuracyNote = "LiDAR-derived 5m DEM class; typical vertical accuracy around 0.3 m at 95% confidence."
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
                }
                else
                {
                    r2Attempt["success"] = false;
                    r2Attempt["reason"] = "No matching tile/elevation value available for coordinate.";
                    attempts.Add(r2Attempt);

                    if (_settings.R2Only || r2OnlyMode)
                    {
                        throw new Exception(JsonSerializer.Serialize(new
                        {
                            error = "R2-only mode enabled and no usable tile/elevation was available for this coordinate.",
                            attempts
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                r2Attempt["success"] = false;
                r2Attempt["reason"] = ex.Message;
                attempts.Add(r2Attempt);

                if (_settings.R2Only || r2OnlyMode)
                {
                    throw;
                }
            }
        }

        if (_settings.R2Only || r2OnlyMode)
        {
            throw new Exception(JsonSerializer.Serialize(new
            {
                error = "R2-only mode enabled and no usable R2 tile/elevation was available for this coordinate.",
                attempts
            }));
        }

        // 2) Try GA LiDAR identify service.
        if (!gaSrtmOnlyMode)
        {
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
                    lidarAttempt["elevation"] = lidarElevation.Value;

                    if (IsNearZeroLidarElevation(lidarElevation.Value))
                    {
                        lidarAttempt["success"] = false;
                        lidarAttempt["reason"] = "GA LiDAR returned near-zero elevation; falling back to SRTM.";
                        attempts.Add(lidarAttempt);
                    }
                    else
                    {
                        lidarAttempt["success"] = true;
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
                            Upstream = _settings.GaLidarIdentifyUrl,
                            VerticalAccuracy95M = 0.3,
                            VerticalAccuracyNote = "LiDAR-derived DEM class; typical vertical accuracy around 0.3 m at 95% confidence."
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
                }
                else
                {
                    lidarAttempt["success"] = false;
                    lidarAttempt["reason"] = "No usable LiDAR elevation returned.";
                    attempts.Add(lidarAttempt);
                }
            }
            catch (Exception ex)
            {
                lidarAttempt["success"] = false;
                lidarAttempt["reason"] = ex.Message;
                attempts.Add(lidarAttempt);
            }
        }

        // 3) If lidar is required, stop here.
        if (requireLidar)
        {
            throw new Exception(JsonSerializer.Serialize(new
            {
                error = "Lidar was required, but no LiDAR source returned a usable non-near-zero elevation.",
                attempts
            }));
        }

        // 4) Fall back to GA SRTM identify service.
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
                    Upstream = _settings.GaSrtmIdentifyUrl,
                    VerticalAccuracy95M = 7.582,
                    VerticalAccuracyNote = "SRTM fallback; vertical accuracy is terrain-dependent and can be several metres."
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

    private async Task<TileSampleResult?> TrySampleR2TileAsync(double lat, double lon)
    {
        if (!TryGetTileReference(lat, lon, out TileReference tile))
            return null;

        string baseUrl = _settings.R2TileBaseUrl.TrimEnd('/');
        string remoteUrl = $"{baseUrl}/{tile.State}/{tile.TileId}.tif";

        if (!_settings.PersistTileCache)
        {
            using var response = await _httpClient.GetAsync(remoteUrl);
            if (!response.IsSuccessStatusCode)
                return null;

            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            double? inMemoryElevation = SampleTiffAtLatLon(bytes, tile, lat, lon);
            if (!inMemoryElevation.HasValue)
                return null;

            return new TileSampleResult(tile.State, tile.TileId, "(memory)", inMemoryElevation.Value, true);
        }

        string cacheRoot = string.IsNullOrWhiteSpace(_settings.TileCacheDir) ? "Data/tile_cache" : _settings.TileCacheDir;
        string localDir = Path.Combine(cacheRoot, tile.State);
        string localPath = Path.Combine(localDir, $"{tile.TileId}.tif");

        bool downloadedNow = false;
        if (!File.Exists(localPath))
        {
            string key = localPath.ToLowerInvariant();
            SemaphoreSlim gate = TileDownloadLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (!File.Exists(localPath))
                {
                    Directory.CreateDirectory(localDir);
                    using var response = await _httpClient.GetAsync(remoteUrl);
                    if (!response.IsSuccessStatusCode)
                        return null;

                    await using var inStream = await response.Content.ReadAsStreamAsync();
                    await using var outStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await inStream.CopyToAsync(outStream);
                    downloadedNow = true;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        double? elevation = SampleTiffAtLatLon(localPath, tile, lat, lon);
        if (!elevation.HasValue)
            return null;

        return new TileSampleResult(tile.State, tile.TileId, localPath, elevation.Value, downloadedNow);
    }

    private static double? SampleTiffAtLatLon(byte[] tiffBytes, TileReference tile, double lat, double lon)
    {
        using var stream = new MemoryStream(tiffBytes, writable: false);
        using Tiff? tiff = Tiff.ClientOpen("r2-memory-tiff", "r", stream, new MemoryTiffStream());
        if (tiff == null)
            return null;

        return SampleTiffAtLatLonCore(tiff, tile, lat, lon);
    }

    private static double? SampleTiffAtLatLon(string tifPath, TileReference tile, double lat, double lon)
    {
        using Tiff? tiff = Tiff.Open(tifPath, "r");
        if (tiff == null)
            return null;

        return SampleTiffAtLatLonCore(tiff, tile, lat, lon);
    }

    private static double? SampleTiffAtLatLonCore(Tiff tiff, TileReference tile, double lat, double lon)
    {
        int width = tiff.GetField(TiffTag.IMAGEWIDTH)?[0].ToInt() ?? 0;
        int height = tiff.GetField(TiffTag.IMAGELENGTH)?[0].ToInt() ?? 0;
        if (width <= 0 || height <= 0)
            return null;

        int bitsPerSample = tiff.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 32;
        int sampleCount = tiff.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;
        int sampleFormat = tiff.GetField(TiffTag.SAMPLEFORMAT)?[0].ToInt() ?? (int)SampleFormat.IEEEFP;

        double xNorm = (lon - tile.MinLon) / (tile.MaxLon - tile.MinLon);
        double yNorm = (tile.MaxLat - lat) / (tile.MaxLat - tile.MinLat);

        int col = Clamp((int)Math.Floor(xNorm * width), 0, width - 1);
        int row = Clamp((int)Math.Floor(yNorm * height), 0, height - 1);

        int bytesPerSample = Math.Max(1, bitsPerSample / 8);
        int bytesPerPixel = Math.Max(1, bytesPerSample * sampleCount);
        double value;

        if (tiff.IsTiled())
        {
            int tileWidth = tiff.GetField(TiffTag.TILEWIDTH)?[0].ToInt() ?? 0;
            int tileLength = tiff.GetField(TiffTag.TILELENGTH)?[0].ToInt() ?? 0;
            if (tileWidth <= 0 || tileLength <= 0)
                return null;

            int tileX = (col / tileWidth) * tileWidth;
            int tileY = (row / tileLength) * tileLength;
            int tileSize = tiff.TileSize();
            if (tileSize <= 0)
                return null;

            var tileData = new byte[tileSize];
            // ReadTile signature is (buffer, offset, x, y, z, plane).
            // Using x as offset causes out-of-bounds on many tiles.
            int read = tiff.ReadTile(tileData, 0, tileX, tileY, 0, 0);
            if (read <= 0)
                return null;

            int localX = col - tileX;
            int localY = row - tileY;
            int rowStride = tileWidth * bytesPerPixel;
            int pixelOffset = (localY * rowStride) + (localX * bytesPerPixel);
            if (pixelOffset + bytesPerSample > tileData.Length)
                return null;

            value = ReadSampleValue(tileData, pixelOffset, bitsPerSample, sampleFormat);
        }
        else
        {
            int scanlineSize = tiff.ScanlineSize();
            var rowData = new byte[Math.Max(scanlineSize, 1)];
            if (!tiff.ReadScanline(rowData, row))
                return null;

            int pixelOffset = col * bytesPerPixel;
            if (pixelOffset + bytesPerSample > rowData.Length)
                return null;

            value = ReadSampleValue(rowData, pixelOffset, bitsPerSample, sampleFormat);
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
            return null;

        double? noData = TryReadNoDataValue(tiff);
        if (noData.HasValue && Math.Abs(value - noData.Value) < 1e-6)
            return null;

        if (value < -10000 || value > 10000)
            return null;

        return value;
    }

    private static double ReadSampleValue(byte[] rowData, int offset, int bitsPerSample, int sampleFormat)
    {
        if (sampleFormat == (int)SampleFormat.IEEEFP)
        {
            if (bitsPerSample == 32 && offset + 4 <= rowData.Length)
                return BitConverter.ToSingle(rowData, offset);

            if (bitsPerSample == 64 && offset + 8 <= rowData.Length)
                return BitConverter.ToDouble(rowData, offset);
        }

        if (sampleFormat == (int)SampleFormat.INT)
        {
            if (bitsPerSample == 16 && offset + 2 <= rowData.Length)
                return BitConverter.ToInt16(rowData, offset);

            if (bitsPerSample == 32 && offset + 4 <= rowData.Length)
                return BitConverter.ToInt32(rowData, offset);
        }

        if (bitsPerSample == 8 && offset + 1 <= rowData.Length)
            return rowData[offset];

        if (bitsPerSample == 16 && offset + 2 <= rowData.Length)
            return BitConverter.ToUInt16(rowData, offset);

        if (bitsPerSample == 32 && offset + 4 <= rowData.Length)
            return BitConverter.ToUInt32(rowData, offset);

        return double.NaN;
    }

    private static double? TryReadNoDataValue(Tiff tiff)
    {
        const int gdalNoDataTag = 42113;
        FieldValue[]? fields = tiff.GetField((TiffTag)gdalNoDataTag);
        if (fields == null || fields.Length == 0)
            return null;

        string raw = fields[0].ToString();
        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double noData))
            return noData;

        return null;
    }

    private bool TryGetTileReference(double lat, double lon, out TileReference tile)
    {
        tile = new TileReference("", "", 0, 0, 0, 0);

        double tileDeg = _settings.TileDeg <= 0 ? 0.5 : _settings.TileDeg;
        foreach (StateBounds state in SnowStateBounds.Values)
        {
            if (lon < state.MinLon || lon >= state.MaxLon || lat < state.MinLat || lat >= state.MaxLat)
                continue;

            int xIndex = (int)Math.Floor((lon - state.MinLon) / tileDeg);
            int yIndex = (int)Math.Floor((lat - state.MinLat) / tileDeg);
            if (xIndex < 0 || yIndex < 0)
                continue;

            double tileMinLon = state.MinLon + (xIndex * tileDeg);
            double tileMinLat = state.MinLat + (yIndex * tileDeg);
            double tileMaxLon = Math.Min(tileMinLon + tileDeg, state.MaxLon);
            double tileMaxLat = Math.Min(tileMinLat + tileDeg, state.MaxLat);

            string tileId = FormatTileId(tileMinLon, tileMinLat);
            tile = new TileReference(state.State, tileId, tileMinLon, tileMinLat, tileMaxLon, tileMaxLat);
            return true;
        }

        return false;
    }

    private static string FormatTileId(double minLon, double minLat)
    {
        string id = string.Create(
            CultureInfo.InvariantCulture,
            $"lon{minLon:F3}_lat{minLat:F3}");

        return id.Replace("-", "m").Replace(".", "p");
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private async Task<double?> SampleGaLidarAsync(double lat, double lon, Dictionary<string, object> lidarAttempt)
    {
        var probes = new List<Dictionary<string, object>>();
        lidarAttempt["identify_probes"] = probes;

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
                    probe["elevation"] = elevation.Value;

                    if (IsNearZeroLidarElevation(elevation.Value))
                    {
                        probe["success"] = false;
                        probe["reason"] = "LiDAR identify returned near-zero elevation; trying next probe.";
                        probes.Add(probe);
                        continue;
                    }

                    probe["success"] = true;
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
            throw new Exception($"Identify request failed. Status={(int)response.StatusCode}. Body={json}");

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("error", out JsonElement error))
            throw new Exception($"ArcGIS service returned error: {error}");

        if (!doc.RootElement.TryGetProperty("results", out JsonElement results))
            throw new Exception($"Response did not contain 'results'. Raw JSON: {json}");

        foreach (JsonElement result in results.EnumerateArray())
        {
            if (result.TryGetProperty("value", out JsonElement valueElement) &&
                TryParseNumericElement(valueElement, out double value))
            {
                return value;
            }

            if (!result.TryGetProperty("attributes", out JsonElement attributes))
                continue;

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

    private static bool IsNearZeroLidarElevation(double elevation)
    {
        return Math.Abs(elevation) < 1e-3;
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

    private sealed record TileSampleResult(
        string State,
        string TileId,
        string LocalPath,
        double Elevation,
        bool DownloadedNow);

    private sealed record TileReference(
        string State,
        string TileId,
        double MinLon,
        double MinLat,
        double MaxLon,
        double MaxLat);

    private sealed record StateBounds(
        string State,
        double MinLon,
        double MaxLon,
        double MinLat,
        double MaxLat);

    private sealed class MemoryTiffStream : TiffStream
    {
        public override int Read(object clientData, byte[] buffer, int offset, int count)
        {
            var stream = (MemoryStream)clientData;
            return stream.Read(buffer, offset, count);
        }

        public override void Write(object clientData, byte[] buffer, int offset, int count)
        {
            var stream = (MemoryStream)clientData;
            stream.Write(buffer, offset, count);
        }

        public override long Seek(object clientData, long offset, SeekOrigin origin)
        {
            var stream = (MemoryStream)clientData;
            return stream.Seek(offset, origin);
        }

        public override void Close(object clientData)
        {
        }

        public override long Size(object clientData)
        {
            var stream = (MemoryStream)clientData;
            return stream.Length;
        }

    }
}
