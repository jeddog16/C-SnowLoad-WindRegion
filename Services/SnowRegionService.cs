using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using AhdApi.Models;

namespace AhdApi.Services;

public class SnowRegionService
{
    private readonly AppSettings _settings;
    private List<SnowRegionBox>? _boxes;

    public SnowRegionService(IOptions<AppSettings> options)
    {
        _settings = options.Value;
    }

    public string? Classify(double lat, double lon)
    {
        var boxes = LoadBoxes();

        foreach (var box in boxes)
        {
            if (lat >= box.MinLat && lat <= box.MaxLat &&
                lon >= box.MinLon && lon <= box.MaxLon)
            {
                return box.SnowRegion;
            }
        }

        return null;
    }

    public object DebugInfo()
    {
        try
        {
            var boxes = LoadBoxes();
            return new
            {
                fileExists = File.Exists(_settings.SnowRegionXlsxPath),
                path = _settings.SnowRegionXlsxPath,
                boxCount = boxes.Count
            };
        }
        catch (Exception ex)
        {
            return new
            {
                fileExists = File.Exists(_settings.SnowRegionXlsxPath),
                path = _settings.SnowRegionXlsxPath,
                error = ex.Message
            };
        }
    }

    private List<SnowRegionBox> LoadBoxes()
    {
        if (_boxes != null) return _boxes;

        _boxes = new List<SnowRegionBox>();

        if (!File.Exists(_settings.SnowRegionXlsxPath))
            return _boxes;

        using var workbook = new XLWorkbook(_settings.SnowRegionXlsxPath);
        var ws = workbook.Worksheet(1);

        var headerRow = ws.Row(1);
        var headers = new Dictionary<string, int>();

        foreach (var cell in headerRow.CellsUsed())
            headers[cell.GetString().Trim()] = cell.Address.ColumnNumber;

        string[] required =
        {
            "Snow_Load",
            "BoundaryMinLatitude",
            "BoundaryMaxLatitude",
            "BoundaryMinLongitude",
            "BoundaryMaxLongitude"
        };

        foreach (var col in required)
        {
            if (!headers.ContainsKey(col))
                throw new Exception($"Snow region file missing required column: {col}");
        }

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            try
            {
                string snowLoad = row.Cell(headers["Snow_Load"]).GetString().Trim();
                if (string.IsNullOrWhiteSpace(snowLoad))
                    continue;

                _boxes.Add(new SnowRegionBox
                {
                    SnowRegion = snowLoad,
                    MinLat = row.Cell(headers["BoundaryMinLatitude"]).GetDouble(),
                    MaxLat = row.Cell(headers["BoundaryMaxLatitude"]).GetDouble(),
                    MinLon = row.Cell(headers["BoundaryMinLongitude"]).GetDouble(),
                    MaxLon = row.Cell(headers["BoundaryMaxLongitude"]).GetDouble()
                });
            }
            catch
            {
            }
        }

        return _boxes;
    }

    private class SnowRegionBox
    {
        public string SnowRegion { get; set; } = "";
        public double MinLat { get; set; }
        public double MaxLat { get; set; }
        public double MinLon { get; set; }
        public double MaxLon { get; set; }
    }
}