# C-SnowLoad-WindRegion

ASP.NET Core API for:

- converting coordinates to AHD elevation
- converting GNSS ellipsoid height to AHD
- parsing NMEA GGA messages
- returning wind region and snow region data

## Stack

- .NET 10
- ASP.NET Core Web API
- NetTopologySuite for wind region shapefile lookup
- ClosedXML for snow region spreadsheet lookup

## Features

- `GET /health`
- `GET /ahd`
- `GET /ahd_gnss`
- `POST /ahd_from_nmea_gga`
- `GET /wind_debug`
- Swagger UI
- API key protection with `X-API-Key`

## Project structure

- `AhdApi.csproj`
- `Program.cs`
- `Controllers/AhdController.cs`
- `Middleware/ApiKeyMiddleware.cs`
- `Models/`
- `Services/`
- `Data/`

## Data files

The API depends on local data files in `Data/`.

- `Data/AUSGeoid2020_20180201.gtx`
- `Data/snowload.xlsx`
- `Data/wind_regions.zip`
- `Data/wind_regions_unzipped/`

Wind regions are read from the shapefile and returned as codes such as `A1`, `A2`, `A3`, `B1`, `B2`.

## Requirements

- .NET 10 SDK
- internet access for the upstream DEM services

## Local setup

1. Clone the repo.
2. Open a terminal in the repo root.
3. Restore and build:

```powershell
dotnet restore
dotnet build
```

4. Run the API:

```powershell
dotnet run
```

By default, local launch settings use:

- `http://localhost:5151`
- `https://localhost:7150`

## Configuration

Settings are stored in [appsettings.json](/abs/path/c:/Users/JedArmstrong/Documents/GitHub/C-SnowLoad-WindRegion/appsettings.json).

Important values:

- `AppSettings:ApiKey`
- `AppSettings:AusGeoidGtxPath`
- `AppSettings:SnowRegionXlsxPath`
- `AppSettings:WindRegionZipPath`
- `AppSettings:GaLidarIdentifyUrl`
- `AppSettings:GaSrtmIdentifyUrl`

For production, set the API key in environment variables or Azure App Settings instead of relying on the file.

## Authentication

Every request must include the `X-API-Key` header.

PowerShell example:

```powershell
Invoke-RestMethod -Uri "http://localhost:5151/health" -Headers @{ "X-API-Key" = "your-api-key" }
```

`curl.exe` example:

```powershell
curl.exe -H "X-API-Key: your-api-key" http://localhost:5151/health
```

## Endpoints

### `GET /`

Returns basic API metadata and the available routes.

### `GET /health`

Returns:

```json
{
  "status": "ok"
}
```

### `GET /ahd`

Converts latitude and longitude to AHD using the best DEM source available.

Query parameters:

- `lat` required
- `lon` required
- `debug` optional
- `require_lidar` optional

Example:

```powershell
Invoke-RestMethod -Uri "http://localhost:5151/ahd?lat=-33.86&lon=151.21" -Headers @{ "X-API-Key" = "your-api-key" }
```

Typical response fields:

- `ahdM`
- `source`
- `verticalDatum`
- `windRegion`
- `snowRegion`
- `isSnowLoadRegion`

### `GET /ahd_gnss`

Converts ellipsoid height to AHD.

Query parameters:

- `lat` required
- `lon` required
- `h_ellipsoid_m` required

Example:

```powershell
Invoke-RestMethod -Uri "http://localhost:5151/ahd_gnss?lat=-33.86&lon=151.21&h_ellipsoid_m=123.45" -Headers @{ "X-API-Key" = "your-api-key" }
```

### `POST /ahd_from_nmea_gga`

Body:

```json
{
  "nmea": "$GPGGA,...."
}
```

Example:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5151/ahd_from_nmea_gga" -Headers @{ "X-API-Key" = "your-api-key" } -ContentType "application/json" -Body '{"nmea":"$GPGGA,...."}'
```

### `GET /wind_debug`

Returns debug information for the loaded wind and snow region datasets.

Example:

```powershell
Invoke-RestMethod -Uri "http://localhost:5151/wind_debug" -Headers @{ "X-API-Key" = "your-api-key" }
```

## Swagger

Swagger is available locally at:

- `http://localhost:5151/swagger`
- `https://localhost:7150/swagger`

## Azure hosting

This app can be hosted in Azure App Service. A database is not required for the current version because the app reads from local files in `Data/`.

Basic flow:

1. Push the project to GitHub.
2. Create an Azure App Service for a `.NET` web app.
3. Deploy the app from Visual Studio, VS Code, GitHub Actions, or Azure Deployment Center.
4. In Azure App Service, set app settings such as:

```text
AppSettings__ApiKey
AppSettings__AusGeoidGtxPath
AppSettings__SnowRegionXlsxPath
AppSettings__WindRegionZipPath
```

5. Test the deployed URL with the `X-API-Key` header.

## Notes

- Wind regions come from the shapefile `region` field and should return values like `A1`, `A2`, `A3`, `B2`.
- Snow regions come from `snowload.xlsx`.
- The API calls upstream DEM services, so hosted environments must allow outbound HTTPS requests.

## Troubleshooting

### API returns `401`

Your `X-API-Key` header is missing or incorrect.

### API returns `500` with API key message

`AppSettings:ApiKey` is not configured.

### Wind or snow region is blank

- check `GET /wind_debug`
- confirm the `Data` files are present
- confirm the coordinate is within the supported region data

### Local PowerShell `curl` command fails

PowerShell aliases `curl` to `Invoke-WebRequest`. Use `Invoke-RestMethod` or `curl.exe`.

## License

Add a license here if you plan to share or publish the project.
