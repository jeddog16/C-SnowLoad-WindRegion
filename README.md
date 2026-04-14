# C-SnowLoad-WindRegion

`C-SnowLoad-WindRegion` is an ASP.NET Core Web API that takes Australian coordinates and returns:

- AHD elevation data
- wind region classification
- snow load region classification
- some GNSS and NMEA-related conversions

If you are brand new to this project, the simplest way to think about it is:

1. You send the API a latitude and longitude.
2. The API looks up the best elevation source it can find.
3. The API adds wind and snow region information from local datasets.
4. The API returns everything as JSON.

This README is written to explain the project in plain English, including where the files live, what is stored locally, what comes from the internet, and how the whole request flow works.

## What Problem This Project Solves

This project is trying to answer a practical question:

> "For this Australian location, what is the elevation in AHD, and what wind and snow region applies here?"

That matters for surveying, engineering, glazing, structural work, and any workflow where location-based height and region rules need to be applied consistently.

The API currently supports three main types of work:

- looking up AHD elevation from DEM services
- parsing GPS/NMEA GGA strings into usable coordinates and heights
- attaching wind region and snow region metadata to a coordinate

## Important Terms

If the vocabulary is unfamiliar, this quick glossary will help:

- `AHD`: Australian Height Datum. A standard Australian height reference.
- `DEM`: Digital Elevation Model. A grid of elevation values.
- `LiDAR`: high-resolution elevation data collected with laser scanning.
- `SRTM`: a coarser elevation dataset used here as a fallback.
- `GNSS`: satellite positioning data such as GPS.
- `NMEA GGA`: a common GPS sentence format that contains position and altitude information.
- `R2`: Cloudflare object storage. In this project it is used as a source for pre-generated DEM tiles.
- `Shapefile`: a GIS file format used here for wind regions.

## What The API Can Do

The API exposes these routes:

- `GET /`
  Returns a simple summary of the API and the available routes.
- `GET /health`
  Basic health check.
- `GET /ahd`
  Looks up elevation for a latitude and longitude and adds wind and snow region data.
- `POST /ahd_bulk_csv`
  Accepts a CSV file with `lat` and `lon` columns and returns a results CSV.
- `GET /ahd_gnss`
  Accepts ellipsoid height and returns a response in the same output format as the other endpoints.
- `POST /ahd_from_nmea_gga`
  Accepts a raw NMEA GGA sentence, parses it, then runs the GNSS conversion path.
- `GET /wind_debug`
  Shows whether the wind and snow datasets loaded correctly.

## What Is In This Repository

At a high level, the repository has four main parts:

1. The ASP.NET Core API.
2. The local GIS/data files under `Data/`.
3. The Docker setup used to package the API.
4. The Cloudflare Worker + Wrangler files used to deploy the containerized app to Cloudflare.

## Folder And File Map

Here is the important structure, with plain-English descriptions:

```text
C-SnowLoad-WindRegion/
|-- AhdApi.csproj
|-- Program.cs
|-- appsettings.json
|-- Dockerfile
|-- wrangler.jsonc
|-- package.json
|-- Controllers/
|   `-- AhdController.cs
|-- Middleware/
|   `-- ApiKeyMiddleware.cs
|-- Models/
|   |-- AhdResponse.cs
|   |-- AppSetting.cs
|   |-- DemLookupMode.cs
|   `-- NmeaRequest.cs
|-- Services/
|   |-- DataDownloadService.cs
|   |-- DemService.cs
|   |-- GnssService.cs
|   |-- NmeaServices.cs
|   |-- RegionService.cs
|   |-- SnowRegionService.cs
|   `-- WindRegionService.cs
|-- Data/
|   |-- AUSGeoid2020_20180201.gtx
|   |-- snowload.xlsx
|   |-- wind_regions.zip
|   `-- wind_regions_unzipped/
|-- cloudflare-worker/
|   `-- src/
|       `-- index.js
|-- scripts/
|   `-- check-docker.mjs
|-- bin/
|-- obj/
|-- node_modules/
`-- .wrangler/
```

## What Each Main File Does

### API entry and wiring

- `Program.cs`
  Starts the ASP.NET application, registers services, enables Swagger, downloads missing data files if URLs are configured, and turns on API key middleware.
- `AhdApi.csproj`
  Defines the .NET project, NuGet packages, and copies the `Data/` directory into build and publish output.
- `appsettings.json`
  Holds the default app configuration such as data file paths, upstream DEM URLs, and tile-cache settings.

### Request handling

- `Controllers/AhdController.cs`
  Contains all public HTTP endpoints.
- `Middleware/ApiKeyMiddleware.cs`
  Blocks requests that do not provide the correct API key in `X-API-Key` or `api_key`.

### Core business logic

- `Services/DemService.cs`
  Main elevation lookup logic. Chooses between R2 tile cache, GA LiDAR, and GA SRTM.
- `Services/RegionService.cs`
  Adds wind and snow region information to the response.
- `Services/WindRegionService.cs`
  Reads the wind-region shapefile and performs point-in-polygon checks.
- `Services/SnowRegionService.cs`
  Reads the Excel file and checks whether a point sits inside one of the configured snow bounding boxes.
- `Services/NmeaServices.cs`
  Parses NMEA GGA strings and converts NMEA coordinates into decimal latitude and longitude.
- `Services/GnssService.cs`
  Handles the GNSS conversion endpoint.
- `Services/DataDownloadService.cs`
  Downloads missing data files if download URLs are provided in config.

### Cloudflare and deployment

- `Dockerfile`
  Builds and packages the ASP.NET app into a container image.
- `wrangler.jsonc`
  Tells Cloudflare Wrangler how to deploy the Worker and container.
- `cloudflare-worker/src/index.js`
  The Worker that receives public requests and forwards them to the Cloudflare container.
- `package.json`
  Defines the Wrangler scripts used for Cloudflare deployment.
- `scripts/check-docker.mjs`
  Verifies Docker is reachable before Wrangler tries to deploy.

### Generated or local-only folders

- `bin/`
  Build output created by .NET.
- `obj/`
  Intermediate build files created by .NET.
- `node_modules/`
  Installed Node packages for Wrangler and Cloudflare tooling.
- `.wrangler/`
  Local Wrangler state and artifacts.

## Where Data Is Stored

One of the most important things to understand about this project is that it does **not** use a database for its main data.

Most of the project data is stored as files.

### Data stored in the repo

These files live under `Data/` and are part of the local application data:

- `Data/AUSGeoid2020_20180201.gtx`
  Geoid model file intended for GNSS-to-AHD conversion.
- `Data/snowload.xlsx`
  Spreadsheet used to classify snow load regions.
- `Data/wind_regions.zip`
  Zip archive containing the wind region shapefile dataset.
- `Data/wind_regions_unzipped/`
  Extracted shapefile files used by the wind-region service.

### Data created at runtime

These locations may be created or updated while the app is running:

- `Data/wind_regions_unzipped/`
  If the zip exists but the extracted folder does not, the app can extract it on startup.
- `Data/tile_cache/`
  Optional on-disk cache for downloaded DEM tiles when `PersistTileCache` is turned on.
- `bin/` and `obj/`
  Build output created by `dotnet build` or `dotnet run`.
- `node_modules/`
  Created by `npm install`.
- `.wrangler/`
  Created by Wrangler when using the Cloudflare workflow.

### Data not stored in the repo

- API secrets should not be committed to git.
- `.env` is ignored by git and is only for your local machine.
- Cloudflare secrets such as `API_KEY` are stored in Cloudflare when you deploy there.

## There Is No Database

This project currently has:

- no SQL Server
- no SQLite database for application data
- no Entity Framework
- no ORM
- no user accounts table
- no persistent business-data store

That makes the app simple to move around, but it also means the app depends heavily on:

- local files in `Data/`
- live calls to external elevation services
- optional local tile caching if you enable it

## How A Request Moves Through The App

When a request arrives, this is the basic flow:

```text
HTTP request
  -> ApiKeyMiddleware
  -> AhdController
  -> one or more Services
  -> local files and/or external services
  -> JSON or CSV response
```

More specifically:

1. The request hits `ApiKeyMiddleware`.
2. The middleware checks for `X-API-Key` or `api_key`.
3. If the key is valid, the request reaches `AhdController`.
4. The controller calls the correct service based on the route.
5. The service loads local data, calls external APIs, or both.
6. `RegionService` adds wind and snow region fields where relevant.
7. The API returns a response.

## How The Main Elevation Endpoint Works

The most important endpoint is `GET /ahd`.

It accepts:

- `lat`
- `lon`
- optional `debug`
- optional `require_lidar`
- optional `dem_mode`

### The lookup order

`DemService` tries sources in this order:

1. `R2 tile cache`
   If enabled, the API first checks whether the point falls into a supported snow-state tile area and tries to sample a GeoTIFF tile from the configured R2 base URL.
2. `GA LiDAR`
   If no usable tile result is found, the API queries Geoscience Australia LiDAR identify services.
3. `GA SRTM`
   If LiDAR does not return a usable value and LiDAR is not strictly required, the API falls back to the Geoscience Australia SRTM identify service.

### What happens after elevation is found

Once the elevation source returns a usable height:

- the response is marked as `Method = "dem"`
- the source and upstream URL are recorded
- `RegionService` adds:
  - `windRegion`
  - `snowRegion`
  - `isSnowLoadRegion`
  - `windRegionError` if wind classification fails

### What `debug=true` does

If you call `/ahd` with `debug=true`, the response includes an `extra.attempts` object showing which sources were tried and why they succeeded or failed.

### What `require_lidar=true` does

If `require_lidar=true`, the API refuses to fall back to SRTM. That means the request fails if no usable LiDAR result is found.

### What `dem_mode` does

The code currently supports these modes:

- `hybrid`
  Normal behavior. Try multiple sources in order.
- `r2_only`
  Only use the R2 tile path.
- `ga_srtm_identify`
  Skip LiDAR and use the SRTM identify service path.

## How Wind Region Lookup Works

Wind region lookup is file-based and local.

### Data source

The wind data comes from the shapefile set inside:

- `Data/wind_regions.zip`
- `Data/wind_regions_unzipped/`

### Process

1. `WindRegionService` loads the shapefile.
2. Each polygon and its attributes are stored in memory.
3. For a requested point, the service checks which polygon covers that point.
4. If there is no direct match, it tries the nearest polygon within a small fallback distance.
5. It extracts a region code such as `A1`, `A2`, `B2`, or similar.

### Storage behavior

- The source data lives on disk in `Data/`.
- The parsed region shapes are kept in memory after loading.

## How Snow Region Lookup Works

Snow region lookup is also file-based and local.

### Data source

The snow data comes from:

- `Data/snowload.xlsx`

### Process

1. `SnowRegionService` reads the first worksheet.
2. It looks for required columns like:
   - `Snow_Load`
   - `BoundaryMinLatitude`
   - `BoundaryMaxLatitude`
   - `BoundaryMinLongitude`
   - `BoundaryMaxLongitude`
3. Each row becomes a simple bounding box.
4. A point is classified by checking whether it falls inside one of those boxes.

### Storage behavior

- The spreadsheet is stored on disk in `Data/`.
- The parsed boxes are cached in memory after the first load.

## How NMEA Parsing Works

The `POST /ahd_from_nmea_gga` endpoint accepts a JSON body like this:

```json
{
  "nmea": "$GPGGA,123456.00,3350.1234,S,15112.5678,E,1,08,1.0,123.4,M,25.0,M,,*47"
}
```

`NmeaService` then:

1. checks that the sentence is a GGA sentence
2. extracts latitude, longitude, altitude, and geoid separation
3. converts NMEA coordinates into decimal degrees
4. calculates ellipsoid height as:

```text
ellipsoid height = altitude above geoid + geoid separation
```

That result is then passed into the GNSS conversion path.

## Important Limitation: GNSS To AHD Is Not Fully Implemented Yet

This is important enough to call out clearly.

`GET /ahd_gnss` and `POST /ahd_from_nmea_gga` currently go through `GnssService`, but that service is still a placeholder.

Right now it:

- sets `AhdM = hEllipsoidM`
- sets `NAhdM = 0`
- labels the response as GNSS-based

That means it is **not yet doing a real geoid-based conversion** to AHD.

The repo already contains the file that would likely be used for a proper geoid correction:

- `Data/AUSGeoid2020_20180201.gtx`

But that file is not currently used by the live GNSS conversion code.

If someone reads only one warning in this README, it should be this one:

> The DEM-based `/ahd` route is the real working elevation lookup path. The GNSS conversion path is not finished yet.

## External Services This API Calls

Not all data comes from local files.

The elevation lookup also depends on remote services configured in `appsettings.json`, including:

- Geoscience Australia LiDAR identify service
- Geoscience Australia SRTM identify service
- optional Cloudflare R2-hosted DEM tiles

That means:

- local wind and snow region lookup can work from files
- DEM elevation lookup depends on network access

## Configuration

The main config file is:

- `appsettings.json`

The `AppSettings` section controls the project-specific behavior.

### Important config values

- `AppSettings__ApiKey`
  The API key required for incoming requests.
- `AppSettings__AusGeoidGtxPath`
  Path to the GTX geoid file.
- `AppSettings__SnowRegionXlsxPath`
  Path to the snow spreadsheet.
- `AppSettings__WindRegionZipPath`
  Path to the wind region zip file.
- `AppSettings__AusGeoidGtxUrl`
  Optional download URL if the GTX file is missing.
- `AppSettings__SnowRegionXlsxUrl`
  Optional download URL if the snow spreadsheet is missing.
- `AppSettings__WindRegionZipUrl`
  Optional download URL if the wind zip is missing.
- `AppSettings__GaLidarIdentifyUrl`
  Upstream LiDAR identify URL.
- `AppSettings__GaSrtmIdentifyUrl`
  Upstream SRTM identify URL.
- `AppSettings__EnableR2TileFetch`
  Enables the R2 tile lookup path.
- `AppSettings__R2Only`
  Forces the API to use only R2 tiles.
- `AppSettings__DemLookupMode`
  Default DEM strategy.
- `AppSettings__PersistTileCache`
  If `true`, downloaded tiles are cached to disk.
- `AppSettings__R2TileBaseUrl`
  Base URL for tile downloads.
- `AppSettings__TileCacheDir`
  Local folder for persistent tile cache.
- `AppSettings__TileDeg`
  Tile size used when forming tile IDs.

### How .NET environment variable names work here

ASP.NET Core maps nested configuration using double underscores.

For example:

```powershell
$env:AppSettings__ApiKey = "change-this-secret-key"
```

means:

```json
{
  "AppSettings": {
    "ApiKey": "change-this-secret-key"
  }
}
```

## Local Development Setup

### Requirements

For local API development you need:

- .NET 10 SDK
- internet access for upstream DEM services

For Cloudflare deployment you also need:

- Node.js
- Docker Desktop or another Docker-compatible engine
- a Cloudflare account with access to Workers/Containers

### Easiest local run

Set an API key in your current terminal session:

```powershell
$env:AppSettings__ApiKey = "change-this-secret-key"
```

Start the API:

```powershell
dotnet run --urls http://localhost:5099
```

Then open:

- `http://localhost:5099/swagger`
- `http://localhost:5099/health`

### Why `--urls` is useful

This repo does not include a committed `launchSettings.json`, so using `--urls` makes the local address explicit and predictable for anyone following the README.

## How To Call The API

Every request needs one of these:

- `X-API-Key` header
- or `api_key` query parameter

The header is the cleaner option and should be your default.

### Health check

```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5099/health" `
  -Headers @{ "X-API-Key" = "change-this-secret-key" }
```

### AHD lookup by coordinate

```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5099/ahd?lat=-33.8688&lon=151.2093&debug=true" `
  -Headers @{ "X-API-Key" = "change-this-secret-key" }
```

### Bulk CSV lookup

Input CSV:

```csv
lat,lon
-33.8688,151.2093
-37.8136,144.9631
```

PowerShell:

```powershell
$headers = @{ "X-API-Key" = "change-this-secret-key" }
$form = @{ file = Get-Item ".\sample-points.csv" }

Invoke-WebRequest `
  -Method Post `
  -Uri "http://localhost:5099/ahd_bulk_csv" `
  -Headers $headers `
  -Form $form `
  -OutFile ".\ahd_bulk_results.csv"
```

### NMEA GGA example

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5099/ahd_from_nmea_gga" `
  -Headers @{ "X-API-Key" = "change-this-secret-key" } `
  -ContentType "application/json" `
  -Body '{"nmea":"$GPGGA,123456.00,3350.1234,S,15112.5678,E,1,08,1.0,123.4,M,25.0,M,,*47"}'
```

### Dataset debug endpoint

```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5099/wind_debug" `
  -Headers @{ "X-API-Key" = "change-this-secret-key" }
```

## What The Responses Contain

The main response model includes fields like:

- `lat`
- `lon`
- `hEllipsoidM`
- `ahdM`
- `nAhdM`
- `method`
- `verticalDatum`
- `source`
- `sourceType`
- `upstream`
- `verticalAccuracy95M`
- `verticalAccuracyNote`
- `windRegion`
- `snowRegion`
- `isSnowLoadRegion`
- `windRegionError`
- `extra`

Not every field is filled for every endpoint.

For example:

- DEM lookups usually populate `ahdM`, `source`, and accuracy notes.
- GNSS responses include `hEllipsoidM`.
- `extra` is mainly used for debug details.

## How Startup Works

On startup, the app does a few important things:

1. registers all services with dependency injection
2. enables Swagger/OpenAPI
3. creates an app scope
4. calls `DataDownloadService.EnsureDataFilesAsync()`
5. starts the middleware pipeline

### What `DataDownloadService` actually does

It checks whether optional download URLs are configured for:

- the GTX file
- the snow spreadsheet
- the wind zip

If a URL is configured and the local file is missing, it downloads the file.

If the wind zip exists but the extracted folder is missing, it extracts the zip.

In the current checked-in config, the URLs are blank, so the app mostly relies on the files already present in `Data/`.

## Swagger

Swagger UI is enabled in all environments by the current code.

If you start the app on `http://localhost:5099`, Swagger will be at:

- `http://localhost:5099/swagger`

Swagger is useful if you want to explore the endpoints without writing curl or PowerShell commands.

## Cloudflare Deployment

This repo already includes a Cloudflare deployment path using:

- a Docker container for the ASP.NET Core app
- a Cloudflare Worker that forwards public requests into that container

### The files involved

- `Dockerfile`
  Builds the .NET app into a container image.
- `wrangler.jsonc`
  Declares the Worker and container setup.
- `cloudflare-worker/src/index.js`
  Defines the Worker behavior.
- `package.json`
  Defines `npm run deploy` and `npm run dev`.

### What the Worker does

The Worker:

1. accepts the incoming request
2. checks whether an `api_key` query parameter is present
3. if needed, copies that value into the `X-API-Key` header
4. forwards the request to the named Cloudflare container instance

### Where the data lives in the container deployment

When the app is built into a container:

- the published ASP.NET app is copied into `/app`
- the runtime data files are included in the publish output, but the duplicate `Data/wind_regions.zip` and the currently-unused `Data/AUSGeoid2020_20180201.gtx` are excluded to keep the image smaller
- the API key is passed in through Cloudflare as `AppSettings__ApiKey`

So in the Cloudflare deployment path:

- source code lives in this repo
- the built app lives inside the container image
- the Worker secret stores the API key
- the runtime container reads the same `Data/` files that were packaged into the image

### Deploy steps

Install Node dependencies:

```powershell
npm install
```

Make sure Docker is running:

```powershell
docker info
```

Log into Cloudflare:

```powershell
npx wrangler login
```

Create the Worker secret:

```powershell
npx wrangler secret put API_KEY
```

Deploy:

```powershell
npm run deploy
```

### Important note about Docker

Wrangler needs Docker available when building this repo's container image from `Dockerfile`.

The repo includes a preflight check in:

- `scripts/check-docker.mjs`

That check runs before `npm run deploy` and `npm run dev`.

## Build And Tooling Outputs

People often ask "what are all these extra folders?"

Here is the short version:

- `bin/`
  Final compiled output for local builds.
- `obj/`
  Temporary/intermediate .NET build files.
- `.codex-build/`
  Local build artifacts created during Codex work in this workspace.
- `node_modules/`
  Installed JavaScript packages for Wrangler.
- `.wrangler/`
  Local Cloudflare Wrangler state.

These are not your source of truth. Your source of truth is the actual project files such as:

- `Program.cs`
- `Controllers/`
- `Services/`
- `Models/`
- `Data/`
- `Dockerfile`
- `wrangler.jsonc`

## Current Limitations And Risks

This section is intentionally blunt so a new developer does not get the wrong impression.

- `GET /ahd_gnss` is not a real finished GNSS-to-AHD conversion yet.
- `POST /ahd_from_nmea_gga` depends on that same unfinished GNSS path.
- DEM elevation depends on external services, so outages or network failures will affect results.
- Wind and snow classification depend on local file formats staying consistent.
- There are no automated tests in this repo right now.
- There is no persistent database-backed cache or job system.

## Troubleshooting

### `401 Unauthorized`

Cause:

- missing API key
- wrong API key

Fix:

- send `X-API-Key`
- or send `api_key`
- make sure it matches `AppSettings__ApiKey`

### `500` with "API key not configured"

Cause:

- `AppSettings__ApiKey` was never set

Fix:

```powershell
$env:AppSettings__ApiKey = "change-this-secret-key"
```

Then restart the app.

### Wind or snow region is blank

Cause:

- the coordinate may be outside the supported region data
- the data files may be missing
- the wind shapefile may not have loaded

Fix:

- call `GET /wind_debug`
- check that `Data/snowload.xlsx` exists
- check that `Data/wind_regions_unzipped/` exists

### DEM lookup fails

Cause:

- upstream DEM service unavailable
- internet access blocked
- requested mode is too strict, such as `require_lidar=true`

Fix:

- retry with `debug=true`
- inspect `extra.attempts`
- try without `require_lidar`
- verify network access to the configured upstream URLs

### Cloudflare deploy fails before upload

Cause:

- Docker is not running or not reachable

Fix:

```powershell
docker info
```

If that fails, start Docker Desktop and try again.

## Recommended Next Improvements

If you plan to keep developing this project, the highest-value improvements are probably:

1. Implement real geoid-based GNSS-to-AHD conversion using the GTX file.
2. Add automated tests for each endpoint and service.
3. Add structured validation and clearer error responses.
4. Add provenance and version notes for the bundled data files.
5. Add a clear deployment story for production environments beyond local/demo use.

## Quick Summary

If you only remember five things about this repo, remember these:

1. It is a file-backed API, not a database-backed app.
2. The main working route is `GET /ahd`.
3. Wind and snow data come from local files under `Data/`.
4. DEM elevation comes from external services, with optional R2 tile support.
5. The GNSS conversion path is still incomplete.
