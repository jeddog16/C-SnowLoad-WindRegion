# C-SnowLoad-WindRegion

API for converting GPS / DEM / GNSS / NMEA coordinates to AHD elevation, with wind/snow region info.

## Project structure

- `AhdApi.csproj`
- `Program.cs`
- `Controllers/AhdController.cs`
- `Services/DemService.cs`
- `Services/GnssService.cs`
- `Services/NmeaServices.cs`
- `Services/WindRegionService.cs`
- `Services/SnowRegionService.cs`
- `Middleware/ApiKeyMiddleware.cs`
- `Data/AUSGeoid2020_20180201.gtx`
- `Data/wind_regions_unzipped`
- `Models/NmeaRequest.cs`
- `Models/AhdResponse.cs`

## Requirements

- .NET 8 SDK (or 7 depending target; check project settings)
- Windows, Linux, macOS supported by .NET

## Install/Run

1. In repo root:
   - `dotnet restore`
   - `dotnet build`
2. Run:
   - `dotnet run`
3. Default local URL:
   - `http://localhost:5000`
   - `https://localhost:5001`

## Endpoints

### GET `/`
- Metadata: API name, version, supported endpoints.

### GET `/health`
- Returns `{ "status": "ok" }`

### GET `/ahd`
- Convert lat/lon to AHD via DEM.
- Query args:
  - `lat` (double, required)
  - `lon` (double, required)
  - `debug` (bool, optional, default: `false`)
  - `require_lidar` (bool, optional, default: `false`)
- Example:
  - `GET /ahd?lat=-33.86&lon=151.21`

### GET `/ahd_gnss`
- Convert GNSS ellipsoid height to AHD.
- Query args:
  - `lat` (double, required)
  - `lon` (double, required)
  - `h_ellipsoid_m` (double, required)
- Example:
  - `GET /ahd_gnss?lat=-33.86&lon=151.21&h_ellipsoid_m=123.45`

### POST `/ahd_from_nmea_gga`
- Body:
  ```json
  {
    "nmea": "$GPGGA,...."
  }
  ```
- Returns AHD result plus NMEA geoid and ellipsoid fields.

### GET `/wind_debug`
- Returns debug for wind and snow region lookup.
- Response includes `wind` and `snow` info.

## Error format

Errors return HTTP 500 with JSON:
- `error`
- `detail`

## Data files

- `Data/AUSGeoid2020_20180201.gtx`: geoid model for AHD calculations.
- `Data/wind_regions_unzipped`: region data for wind/snow constraints.

## Usage examples

- AHD:
  ```bash
  curl "http://localhost:5000/ahd?lat=-33.86&lon=151.21&debug=true"
  ```

- GNSS:
  ```bash
  curl "http://localhost:5000/ahd_gnss?lat=-33.86&lon=151.21&h_ellipsoid_m=100"
  ```

- NMEA:
  ```bash
  curl -X POST "http://localhost:5000/ahd_from_nmea_gga" \
    -H "Content-Type: application/json" \
    -d '{"nmea":"$GPGGA,...."}'
  ```

## Tests

- `dotnet test`

## Notes

- Check `Program.cs` for service registration and pipeline.
- If API key middleware is enabled, include required header(s).  
