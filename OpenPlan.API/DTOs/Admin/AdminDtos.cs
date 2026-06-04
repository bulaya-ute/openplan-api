using System.ComponentModel.DataAnnotations;
using OpenPlan.API.Models;

namespace OpenPlan.API.DTOs.Admin;

public record AdminUserResponse(
    Guid Id,
    string Username,
    string DisplayName,
    bool IsAdmin,
    DateTimeOffset? AdminAddedAt,
    Guid? AdminAddedBy,
    DateTimeOffset CreatedAt
);

public record AccessControlSettingsResponse(
    AccessMode AccessMode,
    IEnumerable<AccessControlEntryResponse> Entries
);

public record AccessControlEntryResponse(
    Guid Id,
    IdentifierType IdentifierType,
    string IdentifierValue,
    ListType ListType,
    DateTimeOffset AddedAt,
    Guid AddedBy
);

public record SetAccessModeRequest([Required] AccessMode AccessMode);

public record AddEntryRequest(
    [Required] IdentifierType IdentifierType,
    [Required] string IdentifierValue,
    [Required] ListType ListType
);

public record VersionInfoResponse(
    string Version,
    string SchemaHash,
    IEnumerable<string> Migrations
);

public record AvailableReleaseResponse(
    string Tag,
    DateTimeOffset PublishedAt,
    string Notes
);

public record SwitchVersionRequest(
    [Required] string TargetVersion,
    bool AcknowledgeSchemaChange = false
);

public record SwitchVersionResponse(string Status, string? SchemaWarning, string? CurrentHash, string? TargetHash);

public record BackupRecordResponse(
    Guid Id,
    string Filename,
    string ApiVersion,
    string SchemaHash,
    DateTimeOffset CreatedAt,
    long SizeBytes
);
