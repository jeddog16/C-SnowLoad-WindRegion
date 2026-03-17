namespace AhdApi.Services;

public class NmeaService
{
    public double NmeaToDecimal(string coord, string hemi)
    {
        if (string.IsNullOrWhiteSpace(coord) || string.IsNullOrWhiteSpace(hemi))
            throw new ArgumentException("Missing NMEA coordinate or hemisphere");

        double value = double.Parse(coord, System.Globalization.CultureInfo.InvariantCulture);
        int degrees = (int)(value / 100);
        double minutes = value - (degrees * 100);
        double dec = degrees + (minutes / 60.0);

        if (hemi.Equals("S", StringComparison.OrdinalIgnoreCase) ||
            hemi.Equals("W", StringComparison.OrdinalIgnoreCase))
        {
            dec = -dec;
        }

        return dec;
    }

    public Dictionary<string, double> ParseGga(string sentence)
    {
        string s = sentence.Trim();

        if (!s.StartsWith("$") || !s.Contains("GGA"))
            throw new ArgumentException("Not a GGA sentence");

        string noChecksum = s.Split('*')[0];
        string[] parts = noChecksum.Split(',');

        if (parts.Length < 12)
            throw new ArgumentException("Incomplete GGA sentence");

        string latStr = parts[2];
        string latHemi = parts[3];
        string lonStr = parts[4];
        string lonHemi = parts[5];
        string altStr = parts[9];
        string geoidSepStr = parts[11];

        if (string.IsNullOrWhiteSpace(altStr) || string.IsNullOrWhiteSpace(geoidSepStr))
            throw new ArgumentException("GGA sentence missing altitude or geoid separation");

        double lat = NmeaToDecimal(latStr, latHemi);
        double lon = NmeaToDecimal(lonStr, lonHemi);
        double altAboveGeoidM = double.Parse(altStr, System.Globalization.CultureInfo.InvariantCulture);
        double geoidSepM = double.Parse(geoidSepStr, System.Globalization.CultureInfo.InvariantCulture);
        double hEllipsoidM = altAboveGeoidM + geoidSepM;

        return new Dictionary<string, double>
        {
            ["lat"] = lat,
            ["lon"] = lon,
            ["alt_above_geoid_m"] = altAboveGeoidM,
            ["geoid_sep_m"] = geoidSepM,
            ["h_ellipsoid_m"] = hEllipsoidM
        };
    }
}