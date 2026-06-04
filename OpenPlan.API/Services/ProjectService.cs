using Microsoft.EntityFrameworkCore;
using OpenPlan.API.Data;
using OpenPlan.API.DTOs.Projects;
using OpenPlan.API.Models;

namespace OpenPlan.API.Services;

public class ProjectService(AppDbContext db)
{
    public async Task<List<ProjectResponse>> GetAllAsync(Guid userId)
    {
        var projects = await db.Projects
            .Where(p => p.OwnerId == userId)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        return projects.Select(MapToResponse).ToList();
    }

    public async Task<ProjectResponse?> GetByIdAsync(Guid userId, Guid projectId)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == userId);
        return project == null ? null : MapToResponse(project);
    }

    public async Task<ProjectResponse> CreateAsync(Guid userId, CreateProjectRequest req)
    {
        var project = new Project
        {
            OwnerId = userId,
            Name = req.Name,
            Color = string.IsNullOrEmpty(req.Color) ? "#6366f1" : req.Color,
            SortOrder = req.SortOrder
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return MapToResponse(project);
    }

    public async Task<ProjectResponse?> UpdateAsync(Guid userId, Guid projectId, UpdateProjectRequest req)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == userId);
        if (project == null) return null;

        if (req.Name != null) project.Name = req.Name;
        if (req.Color != null) project.Color = req.Color;
        if (req.IsArchived.HasValue) project.IsArchived = req.IsArchived.Value;
        if (req.SortOrder.HasValue) project.SortOrder = req.SortOrder.Value;
        project.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return MapToResponse(project);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid projectId)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == userId);
        if (project == null) return false;

        db.Projects.Remove(project);
        await db.SaveChangesAsync();
        return true;
    }

    private static ProjectResponse MapToResponse(Project p) => new(
        p.Id, p.OwnerId, p.Name, p.Color, p.IsArchived, p.SortOrder, p.CreatedAt, p.UpdatedAt
    );
}
