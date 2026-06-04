using Microsoft.EntityFrameworkCore;
using OpenPlan.API.Models;

namespace OpenPlan.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<AccessControlEntry> AccessControlEntries => Set<AccessControlEntry>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Theme).HasDefaultValue("system");
        });

        model.Entity<TaskItem>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Weight).HasDefaultValue(1.0f);
            e.Property(t => t.Priority).HasConversion<string>();
            e.Property(t => t.Status).HasConversion<string>().HasColumnType("text");
            e.Property(t => t.TaskType).HasConversion<string>();

            e.HasOne(t => t.Owner)
                .WithMany(u => u.Tasks)
                .HasForeignKey(t => t.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(t => t.Project)
                .WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(t => t.Parent)
                .WithMany(t => t.Children)
                .HasForeignKey(t => t.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Project>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasOne(p => p.Owner)
                .WithMany(u => u.Projects)
                .HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Admin>(e =>
        {
            e.HasKey(a => a.UserId);
            e.Property(a => a.AccessLevel).HasConversion<string>();

            e.HasOne(a => a.User)
                .WithOne(u => u.Admin)
                .HasForeignKey<Admin>(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.AddedByUser)
                .WithMany()
                .HasForeignKey(a => a.AddedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<AccessControlEntry>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.IdentifierType).HasConversion<string>();
            e.Property(a => a.ListType).HasConversion<string>();
            e.HasIndex(a => new { a.IdentifierType, a.IdentifierValue, a.ListType }).IsUnique();

            e.HasOne(a => a.AddedByUser)
                .WithMany()
                .HasForeignKey(a => a.AddedBy)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<AppSettings>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.AccessMode).HasConversion<string>();
        });
    }
}
