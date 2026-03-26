using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Microsoft.Extensions.Hosting;

namespace AhdApi.Services;

public class WindRegionService
{
    private const double NearestRegionFallbackDistanceDegrees = 0.2;
    private readonly string dataPath;
    private readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326);
    private readonly List<(Geometry Shape, IReadOnlyDictionary<string, object> Attributes)> _regions = new();

        public WindRegionService(IHostEnvironment env)
        {
            dataPath = Path.Combine(env.ContentRootPath, "Data", "wind_regions_unzipped");
            LoadWindRegions();
        }

        private void LoadWindRegions()
        {
            if (_regions.Count > 0) return;

            if (!Directory.Exists(dataPath))
            {
                // if zip exists, extract once
                var zipFile = Path.Combine(Path.GetDirectoryName(dataPath) ?? ".", "wind_regions.zip");
                if (File.Exists(zipFile))
                {
                    ZipFile.ExtractToDirectory(zipFile, dataPath, overwriteFiles: true);
                }
            }

            if (!Directory.Exists(dataPath))
                throw new DirectoryNotFoundException($"Wind region folder missing: {dataPath}");

            var shpFile = Directory.EnumerateFiles(dataPath, "*.shp", SearchOption.AllDirectories).FirstOrDefault();
            if (shpFile == null)
                throw new FileNotFoundException("No .shp file found under " + dataPath);

            using var reader = new ShapefileDataReader(shpFile, _geometryFactory);
            var header = reader.DbaseHeader;

            while (reader.Read())
            {
                var geometry = reader.Geometry;
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                var attrs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < header.NumFields; i++)
                {
                    // ShapefileDataReader includes the geometry in ordinal 0, while the
                    // DBF header fields start at the first attribute column.
                    attrs[header.Fields[i].Name] = values[i + 1];
                }

                _regions.Add((geometry, attrs));
            }
        }

        public IReadOnlyDictionary<string, object>? GetRegion(double lat, double lon)
        {
            var pt = _geometryFactory.CreatePoint(new Coordinate(lon, lat)); // lon/x, lat/y
            var match = _regions.FirstOrDefault(r => r.Shape != null && r.Shape.Covers(pt));
            if (match.Attributes != null)
                return match.Attributes;

            // Some coordinates fall just offshore or on polygon seams. In that case,
            // use the nearest region if it is still reasonably close to the point.
            var nearest = _regions
                .Where(r => r.Shape != null)
                .Select(r => new
                {
                    r.Attributes,
                    Distance = r.Shape!.Distance(pt)
                })
                .OrderBy(r => r.Distance)
                .FirstOrDefault();

            return nearest != null && nearest.Distance <= NearestRegionFallbackDistanceDegrees
                ? nearest.Attributes
                : null;
        }

        public string? Classify(double lat, double lon)
        {
            var region = GetRegion(lat, lon);
            if (region == null) return null;

            // Prefer common wind zone field names
            string? zoneValue = null;
            string[] candidateFields =
            {
                "region",
                "wind_region",
                "windregion",
                "wind_zone",
                "windzone",
                "zone",
                "REGION",
                "WIND_REGION",
                "WINDREGION",
                "WIND_ZONE",
                "WINDZONE",
                "ZONE"
            };

            foreach (var field in candidateFields)
            {
                if (region.TryGetValue(field, out var value) && value != null)
                {
                    zoneValue = value.ToString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(zoneValue))
            {
                // fallback: first non-null attribute matching common wind region codes
                foreach (var value in region.Values)
                {
                    var str = value?.ToString();
                    if (string.IsNullOrWhiteSpace(str)) continue;
                    var trimmed = str.Trim();
                    if (Regex.IsMatch(trimmed, @"^[ABCD](\d+)?$", RegexOptions.IgnoreCase) ||
                        Regex.IsMatch(trimmed, @"^\d+"))
                    {
                        zoneValue = trimmed;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(zoneValue))
                return null;

            return NormalizeWindRegion(zoneValue);
        }

        public object DebugInfo()
        {
            return new
            {
                dataPath,
                loaded = _regions.Count > 0,
                regionCount = _regions.Count
            };
        }

        public static string? NormalizeWindRegion(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            var trimmed = code.Trim().ToUpperInvariant();
            var regionMatch = Regex.Match(trimmed, @"^[ABCD](\d+)?$", RegexOptions.IgnoreCase);
            if (regionMatch.Success)
                return trimmed;

            var numericMatch = Regex.Match(trimmed, @"^(\d+)$", RegexOptions.IgnoreCase);
            if (numericMatch.Success)
                return numericMatch.Groups[1].Value;

            return trimmed;
        }
    }
