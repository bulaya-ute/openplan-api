using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenPlan.API.Data;
using OpenPlan.API.DTOs.Admin;
using OpenPlan.API.Models;

namespace OpenPlan.API.Services;

public class AdminService(AppDbContext db, IConfiguration config, IHttpClientFactory httpFactory)
{
    // ── Users ────────────────────────────────────────────────────────────────

    public async Task<List<AdminUserResponse>> ListUsersAsync()
    {
        var users = await db.Users.Include(u => u.Admin).ToListAsync();
        return users.Select(u => new AdminUserResponse(
            u.Id, u.Username, u.DisplayName,
            u.Admin != null,
            u.Admin?.AddedAt,
            u.Admin?.AddedBy,
            u.CreatedAt
        )).ToList();
    }

    public async Task<bool> GrantAdminAsync(Guid userId, Guid grantedBy)
    {
        if (await db.Admins.AnyAsync(a => a.UserId == userId)) return false;
        if (!await db.Users.AnyAsync(u => u.Id == userId)) return false;

        db.Admins.Add(new Admin { UserId = userId, AddedBy = grantedBy });
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RevokeAdminAsync(Guid userId, Guid requesterId)
    {
        if (userId == requesterId) return false;
        var admin = await db.Admins.FindAsync(userId);
        if (admin == null) return false;

        db.Admins.Remove(admin);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Access Control ───────────────────────────────────────────────────────

    public async Task<AccessControlSettingsResponse> GetSettingsAsync()
    {
        var settings = await db.AppSettings.SingleAsync(s => s.Id == 1);
        var entries = await db.AccessControlEntries.ToListAsync();
        return new AccessControlSettingsResponse(
            settings.AccessMode,
            entries.Select(e => new AccessControlEntryResponse(
                e.Id, e.IdentifierType, e.IdentifierValue, e.ListType, e.AddedAt, e.AddedBy))
        );
    }

    public async Task SetAccessModeAsync(AccessMode mode)
    {
        var settings = await db.AppSettings.SingleAsync(s => s.Id == 1);
        settings.AccessMode = mode;
        await db.SaveChangesAsync();
    }

    public async Task<AccessControlEntryResponse?> AddEntryAsync(
        IdentifierType type, string value, ListType listType, Guid addedBy)
    {
        if (await db.AccessControlEntries.AnyAsync(e =>
            e.IdentifierType == type && e.IdentifierValue == value && e.ListType == listType))
            return null;

        var entry = new AccessControlEntry
        {
            IdentifierType = type,
            IdentifierValue = value.Trim().ToLower(),
            ListType = listType,
            AddedBy = addedBy
        };
        db.AccessControlEntries.Add(entry);
        await db.SaveChangesAsync();
        return new AccessControlEntryResponse(
            entry.Id, entry.IdentifierType, entry.IdentifierValue,
            entry.ListType, entry.AddedAt, entry.AddedBy);
    }

    public async Task<bool> RemoveEntryAsync(Guid id)
    {
        var entry = await db.AccessControlEntries.FindAsync(id);
        if (entry == null) return false;
        db.AccessControlEntries.Remove(entry);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Version Info ─────────────────────────────────────────────────────────

    public async Task<VersionInfoResponse> GetVersionInfoAsync()
    {
        var versionFile = Path.Combine(AppContext.BaseDirectory, "version.json");
        var version = "unknown";
        if (File.Exists(versionFile))
        {
            var doc = JsonDocument.Parse(await File.ReadAllTextAsync(versionFile));
            version = doc.RootElement.GetProperty("version").GetString() ?? "unknown";
        }

        var migrations = await db.Database
            .SqlQueryRaw<string>("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"")
            .ToListAsync();

        var hash = ComputeSchemaHash(migrations);
        return new VersionInfoResponse(version, hash, migrations);
    }

    public async Task<List<AvailableReleaseResponse>> GetAvailableVersionsAsync()
    {
        var repo = config["GitHub:ApiRepo"] ?? "bulaya-ute/openplan-api";
        var token = config["GitHub:Token"];
        var client = httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", "openplan-admin");
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var releases = await client.GetFromJsonAsync<List<GitHubRelease>>(
            $"https://api.github.com/repos/{repo}/releases");

        return (releases ?? []).Select(r => new AvailableReleaseResponse(
            r.TagName,
            r.PublishedAt,
            r.Body ?? string.Empty
        )).ToList();
    }

    public async Task<SwitchVersionResponse> SwitchVersionAsync(SwitchVersionRequest req)
    {
        var current = await GetVersionInfoAsync();

        if (!req.AcknowledgeSchemaChange)
        {
            var targetHash = await FetchTargetSchemaHashAsync(req.TargetVersion);
            if (targetHash != null && targetHash != current.SchemaHash)
            {
                return new SwitchVersionResponse(
                    "schema_warning",
                    $"Target version {req.TargetVersion} has a different database schema.",
                    current.SchemaHash,
                    targetHash);
            }
        }

        var updaterUrl = config["Updater:Url"] ?? "http://127.0.0.1:5050/update";
        var client = httpFactory.CreateClient();
        await client.PostAsJsonAsync(updaterUrl, new { targetVersion = req.TargetVersion });
        return new SwitchVersionResponse("ok", null, null, null);
    }

    // ── Backups ──────────────────────────────────────────────────────────────

    public async Task<List<BackupRecordResponse>> ListBackupsAsync()
    {
        var dir = GetBackupDir();
        if (!Directory.Exists(dir)) return [];

        var results = new List<BackupRecordResponse>();
        foreach (var file in Directory.GetFiles(dir, "*.sql.gz").OrderByDescending(f => f))
        {
            var metaPath = Path.ChangeExtension(file, ".meta.json");
            if (!File.Exists(metaPath)) continue;
            var meta = JsonSerializer.Deserialize<BackupMeta>(await File.ReadAllTextAsync(metaPath));
            if (meta == null) continue;
            results.Add(new BackupRecordResponse(
                meta.Id, Path.GetFileName(file),
                meta.ApiVersion, meta.SchemaHash,
                meta.CreatedAt, new FileInfo(file).Length));
        }
        return results;
    }

    public async Task<BackupRecordResponse> CreateBackupAsync()
    {
        var dir = GetBackupDir();
        Directory.CreateDirectory(dir);

        var info = await GetVersionInfoAsync();
        var timestamp = DateTimeOffset.UtcNow;
        var filename = $"backup-{timestamp:yyyy-MM-ddTHH-mm-ss}.sql.gz";
        var filePath = Path.Combine(dir, filename);

        var connStr = config.GetConnectionString("DefaultConnection")!;
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connStr);

        var env = new Dictionary<string, string>
        {
            ["PGPASSWORD"] = builder.Password ?? string.Empty
        };

        await RunProcessAsync("pg_dump",
            $"-h {builder.Host} -p {builder.Port} -U {builder.Username} -d {builder.Database} -F c -f \"{filePath}\"",
            env);

        var meta = new BackupMeta(Guid.NewGuid(), info.Version, info.SchemaHash, timestamp);
        await File.WriteAllTextAsync(
            Path.ChangeExtension(filePath, ".meta.json"),
            JsonSerializer.Serialize(meta));

        return new BackupRecordResponse(
            meta.Id, filename, meta.ApiVersion, meta.SchemaHash, meta.CreatedAt,
            new FileInfo(filePath).Length);
    }

    public async Task<(bool Success, string? Error)> RestoreBackupAsync(Guid id)
    {
        var dir = GetBackupDir();
        var allMeta = Directory.GetFiles(dir, "*.meta.json");
        string? metaPath = null;
        BackupMeta? meta = null;

        foreach (var mp in allMeta)
        {
            var m = JsonSerializer.Deserialize<BackupMeta>(await File.ReadAllTextAsync(mp));
            if (m?.Id == id) { metaPath = mp; meta = m; break; }
        }

        if (meta == null) return (false, "Backup not found.");

        var current = await GetVersionInfoAsync();
        if (meta.SchemaHash != current.SchemaHash)
            return (false, $"Schema mismatch. Backup schema: {meta.SchemaHash[..20]}… Current: {current.SchemaHash[..20]}…");

        var sqlFile = Path.ChangeExtension(metaPath!, ".sql.gz");
        if (!File.Exists(sqlFile)) return (false, "Backup file not found.");

        var connStr = config.GetConnectionString("DefaultConnection")!;
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connStr);
        var env = new Dictionary<string, string> { ["PGPASSWORD"] = builder.Password ?? string.Empty };

        await RunProcessAsync("pg_restore",
            $"-h {builder.Host} -p {builder.Port} -U {builder.Username} -d {builder.Database} --clean --if-exists \"{sqlFile}\"",
            env);

        return (true, null);
    }

    public async Task<bool> DeleteBackupAsync(Guid id)
    {
        var dir = GetBackupDir();
        foreach (var mp in Directory.GetFiles(dir, "*.meta.json"))
        {
            var m = JsonSerializer.Deserialize<BackupMeta>(await File.ReadAllTextAsync(mp));
            if (m?.Id != id) continue;
            var sqlFile = Path.ChangeExtension(mp, ".sql.gz");
            if (File.Exists(sqlFile)) File.Delete(sqlFile);
            File.Delete(mp);
            return true;
        }
        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string GetBackupDir() =>
        config["Backups:Directory"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "openplan", "backups");

    private static string ComputeSchemaHash(IEnumerable<string> migrations)
    {
        var combined = string.Join("|", migrations);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return "sha256:" + Convert.ToHexString(bytes).ToLower();
    }

    private async Task<string?> FetchTargetSchemaHashAsync(string tag)
    {
        try
        {
            var repo = config["GitHub:ApiRepo"] ?? "bulaya-ute/openplan-api";
            var token = config["GitHub:Token"];
            var client = httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "openplan-admin");
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var url = $"https://raw.githubusercontent.com/{repo}/{tag}/version.json";
            var json = await client.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("schemaHash", out var el) ? el.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task RunProcessAsync(string exe, string args, Dictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (env != null)
            foreach (var (k, v) in env)
                psi.EnvironmentVariables[k] = v;

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exe}");
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"{exe} failed: {err}");
        }
    }

    private record BackupMeta(Guid Id, string ApiVersion, string SchemaHash, DateTimeOffset CreatedAt);
    private record GitHubRelease(string TagName, DateTimeOffset PublishedAt, string? Body);
}
