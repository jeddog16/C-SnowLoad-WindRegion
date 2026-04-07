namespace AhdApi.Models;

public class AhdResponse
{
    public string? CodeVersion { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double? HEllipsoidM { get; set; }
    public double? AhdM { get; set; }
    public double? NAhdM { get; set; }
    public string? Method { get; set; }
    public string? VerticalDatum { get; set; }
    public string? Source { get; set; }
    public string? SourceType { get; set; }
    public string? Upstream { get; set; }
    public double? VerticalAccuracy95M { get; set; }
    public string? VerticalAccuracyNote { get; set; }
    public string? WindRegion { get; set; }
    public string? SnowRegion { get; set; }
    public bool IsSnowLoadRegion { get; set; }
    public string? WindRegionError { get; set; }
    public Dictionary<string, object>? Extra { get; set; }
}
