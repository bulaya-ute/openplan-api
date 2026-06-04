using System.ComponentModel.DataAnnotations;

namespace OpenPlan.API.DTOs.Projects;

public record CreateProjectRequest(
    [Required] string Name,
    string Color,
    int SortOrder
);

public record UpdateProjectRequest(
    string? Name,
    string? Color,
    bool? IsArchived,
    int? SortOrder
);

public record ProjectResponse(
    Guid Id,
    Guid OwnerId,
    string Name,
    string Color,
    bool IsArchived,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
