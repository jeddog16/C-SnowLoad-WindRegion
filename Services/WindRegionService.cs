using Microsoft.Extensions.Options;
using AhdApi.Models;

namespace AhdApi.Services;

public class WindRegionService
{
    private readonly AppSettings _settings;

    public WindRegionService(IOptions<AppSettings> options)
    {
        _settings = options.Value;
    }

    public string? Classify(double lat, double lon)
    {
        return null;
    }

    public object DebugInfo()
    {
        return new
        {
            zipExists = File.Exists(_settings.WindRegionZipPath),
            path = _settings.WindRegionZipPath,
            implemented = false,
            message = "Wind shapefile lookup not implemented yet in this phase."
        };
    }
}