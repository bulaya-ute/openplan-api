using System.ComponentModel.DataAnnotations;

namespace OpenPlan.API.DTOs.Auth;

public record RegisterRequest(
    [Required] string Username,
    [Required, MinLength(6)] string Password,
    [Required] string DisplayName
);

public record LoginRequest(
    [Required] string Username,
    [Required] string Password
);

public record AuthResponse(
    string AccessToken,
    Guid UserId,
    string Username,
    string DisplayName,
    bool IsAdmin
);
