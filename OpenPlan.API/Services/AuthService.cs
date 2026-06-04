using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenPlan.API.Data;
using OpenPlan.API.DTOs.Auth;
using OpenPlan.API.Models;

namespace OpenPlan.API.Services;

public class AuthService(AppDbContext db, IConfiguration config)
{
    public async Task<(AuthResponse? Response, string? Error)> RegisterAsync(RegisterRequest req)
    {
        var username = req.Username.Trim().ToLower();

        if (await db.Users.AnyAsync(u => u.Username == username))
            return (null, "Username already in use.");

        var settings = await db.AppSettings.SingleAsync(s => s.Id == 1);

        if (settings.AccessMode == AccessMode.Whitelist)
        {
            var allowed = await db.AccessControlEntries.AnyAsync(e =>
                e.ListType == ListType.Whitelist &&
                ((e.IdentifierType == IdentifierType.Username && e.IdentifierValue == username) ||
                 (e.IdentifierType == IdentifierType.Email && e.IdentifierValue == username)));

            if (!allowed)
                return (null, "Registration is restricted. Contact an administrator.");
        }
        else
        {
            var blocked = await db.AccessControlEntries.AnyAsync(e =>
                e.ListType == ListType.Blacklist &&
                ((e.IdentifierType == IdentifierType.Username && e.IdentifierValue == username) ||
                 (e.IdentifierType == IdentifierType.Email && e.IdentifierValue == username)));

            if (blocked)
                return (null, "This account is not permitted to register.");
        }

        var user = new User
        {
            Username = username,
            DisplayName = req.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password)
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var isFirstUser = !await db.Admins.AnyAsync();
        if (isFirstUser)
        {
            db.Admins.Add(new Admin { UserId = user.Id, AddedBy = null });
            await db.SaveChangesAsync();
        }

        var isAdmin = isFirstUser || await db.Admins.AnyAsync(a => a.UserId == user.Id);
        return (new AuthResponse(GenerateToken(user), user.Id, user.Username, user.DisplayName, isAdmin), null);
    }

    public async Task<(AuthResponse? Response, string? Error)> LoginAsync(LoginRequest req)
    {
        var username = req.Username.Trim().ToLower();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username);

        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return (null, "Invalid credentials.");

        var settings = await db.AppSettings.SingleAsync(s => s.Id == 1);
        var isAdmin = await db.Admins.AnyAsync(a => a.UserId == user.Id);

        if (settings.AccessMode == AccessMode.Whitelist)
        {
            var allowed = isAdmin || await db.AccessControlEntries.AnyAsync(e =>
                e.ListType == ListType.Whitelist &&
                ((e.IdentifierType == IdentifierType.UserId && e.IdentifierValue == user.Id.ToString()) ||
                 (e.IdentifierType == IdentifierType.Username && e.IdentifierValue == username) ||
                 (e.IdentifierType == IdentifierType.Email && e.IdentifierValue == username)));

            if (!allowed)
                return (null, "Access denied.");
        }
        else
        {
            var blocked = !isAdmin && await db.AccessControlEntries.AnyAsync(e =>
                e.ListType == ListType.Blacklist &&
                ((e.IdentifierType == IdentifierType.UserId && e.IdentifierValue == user.Id.ToString()) ||
                 (e.IdentifierType == IdentifierType.Username && e.IdentifierValue == username) ||
                 (e.IdentifierType == IdentifierType.Email && e.IdentifierValue == username)));

            if (blocked)
                return (null, "Access denied.");
        }

        return (new AuthResponse(GenerateToken(user), user.Id, user.Username, user.DisplayName, isAdmin), null);
    }

    private string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("display_name", user.DisplayName)
            ],
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
