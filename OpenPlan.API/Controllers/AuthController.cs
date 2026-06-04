using Microsoft.AspNetCore.Mvc;
using OpenPlan.API.DTOs.Auth;
using OpenPlan.API.Services;

namespace OpenPlan.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var (result, error) = await auth.RegisterAsync(req);
        if (result == null) return Conflict(new { message = error });
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var (result, error) = await auth.LoginAsync(req);
        if (result == null) return Unauthorized(new { message = error });
        return Ok(result);
    }
}
