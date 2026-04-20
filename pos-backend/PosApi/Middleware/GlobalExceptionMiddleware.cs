using System.Text.Json;
using PosApi.Services.Roller;

namespace PosApi.Middleware;

public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (RollerApiException ex)
        {
            logger.LogWarning(ex, "ROLLER API error {StatusCode}", ex.StatusCode);
            context.Response.StatusCode = 502;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "roller_api_error",
                detail = ex.Detail
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "internal_server_error",
                detail = ex.Message
            }));
        }
    }
}
