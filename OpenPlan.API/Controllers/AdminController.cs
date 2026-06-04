using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenPlan.API.Data;
using OpenPlan.API.DTOs.Admin;
using OpenPlan.API.Models;
using OpenPlan.API.Services;

namespace OpenPlan.API.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize]
public class AdminController(AdminService admin, AppDbContext db) : ControllerBase
{
    private async Task<Guid?> GetAdminIdAsync()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return null;
        return await db.Admins.AnyAsync(a => a.UserId == userId) ? userId : null;
    }

    // ── Users ────────────────────────────────────────────────────────────────

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        if (await GetAdminIdAsync() == null) return Forbid();
        return Ok(await admin.ListUsersAsync());
    }

    [HttpPost("users/{id:guid}/grant-admin")]
    public async Task<IActionResult> GrantAdmin(Guid id)
    {
        var adminId = await GetAdminIdAsync();
        if (adminId == null) return Forbid();
        var ok = await admin.GrantAdminAsync(id, adminId.Value);
        return ok ? Ok() : BadRequest(new { message = "User not found or already admin." });
    }

    [HttpDelete("users/{id:guid}/revoke-admin")]
    public async Task<IActionResult> RevokeAdmin(Guid id)
    {
        var adminId = await GetAdminIdAsync();
        if (adminId == null) return Forbid();
        var ok = await admin.RevokeAdminAsync(id, adminId.Value);
        return ok ? Ok() : BadRequest(new { message = "Cannot revoke your own admin status or user is not admin." });
    }

    // ── Access Control ───────────────────────────────────────────────────────

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        if (await GetAdminIdAsync() == null) return Forbid();
        return Ok(await admin.GetSettingsAsync());
    }

    [HttpPut("settings/mode")]
    public async Task<IActionResult> SetMode(SetAccessModeRequest req)
    {
        if (await GetAdminIdAsync() == null) return Forbid();
        await admin.SetAccessModeAsync(req.AccessMode);
        return Ok();
    }

    [HttpPost("access-control")]
    public async Task<IActionResult> AddEntry(AddEntryRequest req)
    {
        var adminId = await GetAdminIdAsync();
        if (adminId == null) return Forbid();
        var entry = await admin.AddEntryAsync(req.IdentifierType, req.IdentifierValue, req.ListType, adminId.Value);
        return entry != null ? Ok(entry) : Conflict(new { message = "Entry already exists." });
    }

    [HttpDelete("access-control/{id:guid}")]
    public async Task<IActionResult> RemoveEntry(Guid id)
    {
        if (await GetAdminIdAsync() == null) return Forbid();
        return await admin.RemoveEntryAsync(id) ? NoContent() : NotFound();
    }

    // ── Version Management ───────────────────────────────────────────────────

    [HttpGet("version")]
    public async Task<IActionResult> GetVersion()
    {
        if (await GetAdminIdAsync() == null) return Forbid();
        return Ok(await admin.GetVersionInfoAsync());
    }

    [HttpGet("version/available")]
    public async Task<IActionResult> GetAvailableVersions()
    {
        if (await GetAdminIdAsync() == null) return Forbid();
        return Ok(await admin.GetAvailableVersionsAsync());
    }

    [HttpPost("version/switch")]
    public async Task<IActionResult> SwitchVersion(SwitchVersionRequest req)
    {
        if (await GetAdminIdAsync() == null) return Forbid();
        var result = await admin.SwitchVersionAsync(req);
        return Ok(result);
    }

    // ── Backups ──────────────────────────────────────────────────────────────

    [HttpGet("backups")]
    public async Task<IActionResult> ListBackups()
    {
        if (await GetAdminIdAsync() == null) return Forbid();
        return Ok(await admin.ListBackupsAsync());
    }

    [HttpPost("backups")]
    public async Task<IActionResult> CreateBackup()
    {
        if (await GetAdminIdAsync() == null) return Forbid();
        var backup = await admin.CreateBackupAsync();
        return Ok(backup);
    }

    [HttpPost("backups/{id:guid}/restore")]
    public async Task<IActionResult> RestoreBackup(Guid id)
    {
        if (await GetAdminIdAsync() == null) return Forbid();
        var (success, error) = await admin.RestoreBackupAsync(id);
        if (!success)
        {
            if (error?.Contains("Schema mismatch") == true)
                return Conflict(new { message = error });
            return NotFound(new { message = error });
        }
        return Ok();
    }

    [HttpDelete("backups/{id:guid}")]
    public async Task<IActionResult> DeleteBackup(Guid id)
    {
        if (await GetAdminIdAsync() == null) return Forbid();
        return await admin.DeleteBackupAsync(id) ? NoContent() : NotFound();
    }
}
