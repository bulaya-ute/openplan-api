using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenPlan.API.Data;
using OpenPlan.API.Models;

namespace OpenPlan.API.Middleware;

public class AccessControlMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        if (!ctx.User.Identity?.IsAuthenticated ?? true)
        {
            await next(ctx);
            return;
        }

        var userIdStr = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var isAdmin = await db.Admins.AnyAsync(a => a.UserId == userId);
        if (isAdmin)
        {
            await next(ctx);
            return;
        }

        var user = await db.Users.FindAsync(userId);
        if (user == null)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var settings = await db.AppSettings.SingleAsync(s => s.Id == 1);
        var username = user.Username;
        var userIdValue = userId.ToString();

        if (settings.AccessMode == AccessMode.Whitelist)
        {
            var allowed = await db.AccessControlEntries.AnyAsync(e =>
                e.ListType == ListType.Whitelist &&
                ((e.IdentifierType == IdentifierType.UserId && e.IdentifierValue == userIdValue) ||
                 (e.IdentifierType == IdentifierType.Username && e.IdentifierValue == username) ||
                 (e.IdentifierType == IdentifierType.Email && e.IdentifierValue == username)));

            if (!allowed)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { message = "Access denied." });
                return;
            }
        }
        else
        {
            var blocked = await db.AccessControlEntries.AnyAsync(e =>
                e.ListType == ListType.Blacklist &&
                ((e.IdentifierType == IdentifierType.UserId && e.IdentifierValue == userIdValue) ||
                 (e.IdentifierType == IdentifierType.Username && e.IdentifierValue == username) ||
                 (e.IdentifierType == IdentifierType.Email && e.IdentifierValue == username)));

            if (blocked)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { message = "Access denied." });
                return;
            }
        }

        await next(ctx);
    }
}
