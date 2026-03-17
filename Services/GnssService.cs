using AhdApi.Models;

namespace AhdApi.Services;

public class GnssService
{
    private readonly RegionService _regionService;

    public GnssService(RegionService regionService)
    {
        _regionService = regionService;
    }

    public AhdResponse GnssToAhd(double lat, double lon, double hEllipsoidM)
    {
        var resp = new AhdResponse
        {
            CodeVersion = "csharp-port-1.0.0",
            Lat = lat,
            Lon = lon,
            HEllipsoidM = hEllipsoidM,
            AhdM = hEllipsoidM,
            NAhdM = 0.0,
            Method = "gnss",
            VerticalDatum = "AHD",
            Source = "placeholder",
            SourceType = "gnss"
        };

        _regionService.AddRegionInfo(resp);

        return resp;
    }
}