using AhdApi.Models;
using Microsoft.Extensions.Options;

namespace AhdApi.Services;

public class DataDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly ILogger<DataDownloadService> _logger;

    public DataDownloadService(
        HttpClient httpClient,
        IOptions<AppSettings> options,
        ILogger<DataDownloadService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task EnsureDataFilesAsync()
    {
        var dataFiles = new[]
        {
            new { Url = _settings.AusGeoidGtxUrl, LocalPath = _settings.AusGeoidGtxPath },
            new { Url = _settings.SnowRegionXlsxUrl, LocalPath = _settings.SnowRegionXlsxPath },
            new { Url = _settings.WindRegionZipUrl, LocalPath = _settings.WindRegionZipPath }
        };

        foreach (var file in dataFiles)
        {
            if (!string.IsNullOrWhiteSpace(file.Url) && !File.Exists(file.LocalPath))
            {
                _logger.LogInformation(
                    "Downloading {FileName} from {Url}",
                    Path.GetFileName(file.LocalPath),
                    file.Url);

                await DownloadFileAsync(file.Url, file.LocalPath);
            }
        }

        if (File.Exists(_settings.WindRegionZipPath))
        {
            var extractPath = Path.Combine(
                Path.GetDirectoryName(_settings.WindRegionZipPath) ?? ".",
                "wind_regions_unzipped");

            if (!Directory.Exists(extractPath))
            {
                _logger.LogInformation("Extracting wind regions from {ZipPath}", _settings.WindRegionZipPath);
                System.IO.Compression.ZipFile.ExtractToDirectory(
                    _settings.WindRegionZipPath,
                    extractPath,
                    overwriteFiles: true);
            }
        }
    }

    private async Task DownloadFileAsync(string url, string localPath)
    {
        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        await using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream);
    }
}
