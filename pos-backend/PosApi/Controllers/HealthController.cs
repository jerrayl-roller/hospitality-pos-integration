using Microsoft.AspNetCore.Mvc;
using PosApi.Services.Roller;

namespace PosApi.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController(RollerTokenService tokenService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try
        {
            await tokenService.GetTokenAsync(ct);
            return Ok(new { status = "ok", rollerConnected = true });
        }
        catch (Exception ex)
        {
            return Ok(new { status = "degraded", rollerConnected = false, error = ex.Message });
        }
    }
}
