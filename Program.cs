using System.Text;
using AhdApi.Middleware;
using AhdApi.Models;
using AhdApi.Services;
using Microsoft.OpenApi;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppSettings>(
    builder.Configuration.GetSection("AppSettings"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var apiKeyScheme = new OpenApiSecurityScheme
    {
        Description = "API Key needed to access the endpoints. Example: X-API-Key: {key}",
        In = ParameterLocation.Header,
        Name = "X-API-Key",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "ApiKeyScheme"
    };

    options.AddSecurityDefinition("ApiKey", apiKeyScheme);

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("ApiKey", document, null)] = new List<string>()
    });
});

builder.Services.AddSingleton<NmeaService>();
builder.Services.AddSingleton<SnowRegionService>();
builder.Services.AddSingleton<WindRegionService>();
builder.Services.AddSingleton<RegionService>();
builder.Services.AddSingleton<GnssService>();
builder.Services.AddHttpClient<DemService>();
builder.Services.AddHttpClient<DataDownloadService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dataDownloadService = scope.ServiceProvider.GetRequiredService<DataDownloadService>();
    await dataDownloadService.EnsureDataFilesAsync();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ApiKeyMiddleware>();

app.MapControllers();

app.Run();
