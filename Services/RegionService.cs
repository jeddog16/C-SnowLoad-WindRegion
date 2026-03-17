using AhdApi.Models;

namespace AhdApi.Services;

public class RegionService
{
    private readonly SnowRegionService _snowRegionService;
    private readonly WindRegionService _windRegionService;

    public RegionService(
        SnowRegionService snowRegionService,
        WindRegionService windRegionService)
    {
        _snowRegionService = snowRegionService;
        _windRegionService = windRegionService;
    }

    public void AddRegionInfo(AhdResponse resp)
    {
        resp.SnowRegion = _snowRegionService.Classify(resp.Lat, resp.Lon);
        resp.IsSnowLoadRegion = !string.IsNullOrWhiteSpace(resp.SnowRegion);

        try
        {
            resp.WindRegion = _windRegionService.Classify(resp.Lat, resp.Lon);
        }
        catch (Exception ex)
        {
            resp.WindRegionError = ex.Message;
        }
    }
}