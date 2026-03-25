using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AhdApi.Models;
using Microsoft.Extensions.Options;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri.Shapefile;

namespace AhdApi.Services;

public class WindRegionService
{
    private readonly AppSettings _settings;
    private readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326);

    private bool _loaded;
    private string? _extractDir;
    private string? _shpPath;
    private string? _regionField;
    private List<IFeature> _features = new();
    private List<string> _fieldNames = new();
    private string? _loadError;

    public WindRegionService(IOptions<AppSettings> options)
    {
        _settings = options.Value;
    }

    public string? Classify(double lat, double lon)
    {
        EnsureLoaded();

        if (!string.IsNullOrWhiteSpace(_loadError))
            throw new Exception(_loadError);

        if (string.IsNullOrWhiteSpace(_regionField))
            throw new Exception("Could not determine the wind region field from the shapefile.");

        var point = _geometryFactory.CreatePoint(new Coordinate(lon, lat));

        foreach (var feature in _features)
        {
            var geom = feature.Geometry;
            if (geom == null)
                continue;

            bool contains = false;

            try
            {
                contains = geom.Covers(point);
            }
            catch
            {
                try
                {
                    contains = geom.Contains(point);
                }
                catch
                {
                    contains = false;
                }
            }

            if (!contains)
                continue;

            var value = feature.Attributes[_regionField];
            if (value == null)
                return null;

            var region = value.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(region) ? null : region;
        }

        return null;
    }

    public object DebugInfo()
    {
        EnsureLoaded();

        return new
        {
            zipExists = File.Exists(_settings.WindRegionZipPath),
            path = _settings.WindRegionZipPath,
            extractDir = _extractDir,
            shpPath = _shpPath,
            loaded = _loaded,
            featureCount = _features.Count,
            fieldNames = _fieldNames,
            chosenRegionField = _regionField,
            loadError = _loadError,
            sampleAttributes = GetSampleAttributes()
        };
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        try
        {
            if (!File.Exists(_settings.WindRegionZipPath))
                throw new FileNotFoundException($"Wind region zip not found: {_settings.WindRegionZipPath}");

            _extractDir = Path.Combine(
                Path.GetTempPath(),
                "AhdApi_WindRegions",
                "as1170windzones");

            if (Directory.Exists(_extractDir))
                Directory.Delete(_extractDir, recursive: true);

            Directory.CreateDirectory(_extractDir);

            ZipFile.ExtractToDirectory(_settings.WindRegionZipPath, _extractDir, overwriteFiles: true);

            _shpPath = Directory
                .GetFiles(_extractDir, "*.shp", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(_shpPath))
                throw new Exception("No .shp file found after extracting wind_regions.zip.");

            _features = Shapefile.ReadAllFeatures(_shpPath).ToList();

            if (_features.Count == 0)
                throw new Exception("The shapefile loaded successfully but contained zero features.");

            _fieldNames = _features
                .First()
                .Attributes
                .GetNames()
                .ToList();

            _regionField = DetectRegionField(_features);

            _loaded = true;
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            _loaded = true;
        }
    }

    private static string? DetectRegionField(List<IFeature> features)
    {
        if (features.Count == 0)
            return null;

        var names = features.First().Attributes.GetNames().ToList();

        string[] preferredNames =
        {
            "REGION",
            "WINDREGION",
            "WIND_REGION",
            "ZONE",
            "ZONENAME",
            "CODE",
            "CLASS"
        };

        foreach (var preferred in preferredNames)
        {
            var exact = names.FirstOrDefault(n => string.Equals(n, preferred, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
        }

        var regex = new Regex(@"^(A|B|C|D)(\d+)?$", RegexOptions.IgnoreCase);

        string? bestField = null;
        int bestScore = -1;

        foreach (var field in names)
        {
            int score = 0;

            foreach (var feature in features.Take(200))
            {
                var raw = feature.Attributes[field]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                if (regex.IsMatch(raw))
                    score++;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestField = field;
            }
        }

        return bestScore > 0 ? bestField : names.FirstOrDefault();
    }

    private Dictionary<string, object?>? GetSampleAttributes()
    {
        var first = _features.FirstOrDefault();
        if (first == null)
            return null;

        var result = new Dictionary<string, object?>();

        foreach (var name in first.Attributes.GetNames())
            result[name] = first.Attributes[name];

        return result;
    }
}