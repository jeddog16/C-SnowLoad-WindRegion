namespace AhdApi.Models;

public class AppSettings
{
    public string ApiKey { get; set; } = "";
    public string AusGeoidGtxPath { get; set; } = "Data/AUSGeoid2020_20180201.gtx";
    public string SnowRegionXlsxPath { get; set; } = "Data/snowload.xlsx";
    public string WindRegionZipPath { get; set; } = "Data/wind_regions.zip";

    public string GaLidarIdentifyUrl { get; set; } = "";
    public string GaSrtmIdentifyUrl { get; set; } = "";
    public string NswLidarImageServer { get; set; } = "";
    public string QldLidarImageServer { get; set; } = "";
    public string VicLidarImageServer { get; set; } = "";
}