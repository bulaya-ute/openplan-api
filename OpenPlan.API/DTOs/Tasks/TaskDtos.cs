using System.ComponentModel.DataAnnotations;
using OpenPlan.API.Models;

namespace OpenPlan.API.DTOs.Tasks;

public record CreateTaskRequest(
    [Required] string Title,
    string? Description,
    Guid? ProjectId,
    Guid? ParentId,
    TaskType TaskType,
    float Weight,
    TaskPriority Priority,
    DateTimeOffset StartAt,
    DateTimeOffset DueAt,
    int SortOrder
);

public record UpdateTaskRequest(
    string? Title,
    string? Description,
    Guid? ProjectId,
    TaskType? TaskType,
    float? Weight,
    TaskPriority? Priority,
    ItemStatus? Status,
    DateTimeOffset? StartAt,
    DateTimeOffset? DueAt,
    int? SortOrder
);

public record TaskResponse(
    Guid Id,
    Guid OwnerId,
    Guid? ProjectId,
    Guid? ParentId,
    string Title,
    string? Description,
    string TaskType,
    float Weight,
    string Priority,
    string EffectivePriority,
    string Status,
    DateTimeOffset StartAt,
    DateTimeOffset DueAt,
    DateTimeOffset? CompletedAt,
    int SortOrder,
    double Progress,
    int CompletedChildCount,
    int TotalChildCount,
    string? NextChildTitle,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<TaskResponse> Children
);
