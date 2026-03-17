using Microsoft.AspNetCore.Mvc;
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

    public AhdController(
        DemService demService,
        GnssService gnssService,
        NmeaService nmeaService)
    {
        _demService = demService;
        _gnssService = gnssService;
        _nmeaService = nmeaService;
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
                "/ahd_gnss",
                "/ahd_from_nmea_gga"
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
}