namespace OpenPlan.API.Models;

public enum TaskType { Parallel, Sequential }
public enum TaskPriority { P1 = 1, P2 = 2, P3 = 3, P4 = 4 }
public enum ItemStatus { Scheduled, Active, Completed, Cancelled }

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskType TaskType { get; set; } = TaskType.Parallel;
    public float Weight { get; set; } = 1.0f;
    public TaskPriority Priority { get; set; } = TaskPriority.P4;
    public ItemStatus Status { get; set; } = ItemStatus.Scheduled;
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int SortOrder { get; set; } = 0;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User Owner { get; set; } = null!;
    public Project? Project { get; set; }
    public TaskItem? Parent { get; set; }
    public ICollection<TaskItem> Children { get; set; } = [];
}
