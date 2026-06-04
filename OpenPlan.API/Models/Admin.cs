namespace OpenPlan.API.Models;

public enum AdminAccessLevel { Admin, SuperAdmin }

public class Admin
{
    public Guid UserId { get; set; }
    public AdminAccessLevel AccessLevel { get; set; } = AdminAccessLevel.Admin;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? AddedBy { get; set; }

    public User User { get; set; } = null!;
    public User? AddedByUser { get; set; }
}
