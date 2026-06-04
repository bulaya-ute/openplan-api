namespace OpenPlan.API.Models;

public enum IdentifierType { UserId, Email, Username }
public enum ListType { Whitelist, Blacklist }
public enum AccessMode { Whitelist, Blacklist }

public class AccessControlEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public IdentifierType IdentifierType { get; set; }
    public string IdentifierValue { get; set; } = string.Empty;
    public ListType ListType { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid AddedBy { get; set; }

    public User AddedByUser { get; set; } = null!;
}

public class AppSettings
{
    public int Id { get; set; } = 1;
    public AccessMode AccessMode { get; set; } = AccessMode.Blacklist;
}
