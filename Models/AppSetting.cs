namespace AhdApi.Models;

public class AppSettings
{
    public string ApiKey { get; set; } = "";
    public string AusGeoidGtxPath { get; set; } = "Data/AUSGeoid2020_20180201.gtx";
    public string SnowRegionXlsxPath { get; set; } = "Data/snowload.xlsx";
    public string WindRegionZipPath { get; set; } = "Data/wind_regions.zip";
    public string AusGeoidGtxUrl { get; set; } = "";
    public string SnowRegionXlsxUrl { get; set; } = "";
    public string WindRegionZipUrl { get; set; } = "";

    public string GaLidarIdentifyUrl { get; set; } = "";
    public string GaSrtmIdentifyUrl { get; set; } = "";
    public string NswLidarImageServer { get; set; } = "";
    public string QldLidarImageServer { get; set; } = "";
    public string VicLidarImageServer { get; set; } = "";

    public bool EnableR2TileFetch { get; set; }
    public bool R2Only { get; set; }
    public bool PersistTileCache { get; set; }
    public string R2TileBaseUrl { get; set; } = "";
    public string TileCacheDir { get; set; } = "Data/tile_cache";
    public double TileDeg { get; set; } = 0.5;
    public string DemLookupMode { get; set; } = "hybrid";
}
