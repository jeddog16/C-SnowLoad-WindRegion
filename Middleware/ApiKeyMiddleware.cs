using System.Text.Json;
using Microsoft.Extensions.Options;
using AhdApi.Models;

namespace AhdApi.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AppSettings _settings;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<AppSettings> options)
    {
        _next = next;
        _settings = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                detail = "API key not configured."
            }));
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-API-Key", out var providedKey) ||
            providedKey != _settings.ApiKey)
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                detail = "Invalid or missing API key."
            }));
            return;
        }

        await _next(context);
    }
}