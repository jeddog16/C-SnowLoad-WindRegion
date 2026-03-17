using AhdApi.Middleware;
using AhdApi.Models;
using AhdApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppSettings>(
    builder.Configuration.GetSection("AppSettings"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<NmeaService>();
builder.Services.AddSingleton<SnowRegionService>();
builder.Services.AddSingleton<WindRegionService>();
builder.Services.AddSingleton<RegionService>();
builder.Services.AddSingleton<GnssService>();

builder.Services.AddHttpClient<DemService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ApiKeyMiddleware>();

app.MapControllers();

app.Run();