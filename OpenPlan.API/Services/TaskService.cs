using Microsoft.EntityFrameworkCore;
using OpenPlan.API.Data;
using OpenPlan.API.DTOs.Tasks;
using OpenPlan.API.Models;

namespace OpenPlan.API.Services;

public class TaskService(AppDbContext db)
{
    public async Task<List<TaskResponse>> GetRootTasksAsync(Guid userId, string view, DateTimeOffset? date = null)
    {
        var today = DateTimeOffset.UtcNow.Date;

        IQueryable<TaskItem> query = db.Tasks
            .Where(t => t.OwnerId == userId && t.ParentId == null);

        query = view switch
        {
            "today" => query.Where(t =>
                t.Status != ItemStatus.Completed && t.Status != ItemStatus.Cancelled &&
                (t.DueAt.Date == today || t.StartAt.Date == today)),
            "upcoming" => query.Where(t =>
                t.Status != ItemStatus.Completed && t.Status != ItemStatus.Cancelled &&
                t.DueAt.Date >= today),
            "inbox" => query.Where(t => t.ProjectId == null),
            "project" => query,
            _ => query
        };

        var tasks = await query
            .OrderBy(t => t.DueAt == DateTimeOffset.MinValue)   // tasks with a due date first
            .ThenBy(t => t.DueAt)                               // closest due date first
            .ThenBy(t => (int)t.Priority)                       // P1 before P4 within same date
            .ToListAsync();
        await LoadChildrenRecursiveAsync(tasks);
        return tasks.Select(MapToResponse).ToList();
    }

    public async Task<List<TaskResponse>> GetProjectTasksAsync(Guid userId, Guid projectId)
    {
        var tasks = await db.Tasks
            .Where(t => t.OwnerId == userId && t.ProjectId == projectId && t.ParentId == null)
            .OrderBy(t => t.DueAt == DateTimeOffset.MinValue)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => (int)t.Priority)
            .ToListAsync();

        await LoadChildrenRecursiveAsync(tasks);
        return tasks.Select(MapToResponse).ToList();
    }

    public async Task<TaskResponse?> GetByIdAsync(Guid userId, Guid taskId)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.OwnerId == userId);
        if (task == null) return null;

        await LoadChildrenRecursiveAsync([task]);
        return MapToResponse(task);
    }

    public async Task<TaskResponse?> CreateAsync(Guid userId, CreateTaskRequest req)
    {
        var task = new TaskItem
        {
            OwnerId = userId,
            ProjectId = req.ProjectId,
            ParentId = req.ParentId,
            Title = req.Title,
            Description = req.Description,
            TaskType = req.TaskType,
            Weight = req.Weight <= 0 ? 1.0f : req.Weight,
            Priority = req.Priority,
            StartAt = req.StartAt,
            DueAt = req.DueAt,
            SortOrder = req.SortOrder
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return await GetByIdAsync(userId, task.Id);
    }

    public async Task<TaskResponse?> UpdateAsync(Guid userId, Guid taskId, UpdateTaskRequest req)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.OwnerId == userId);
        if (task == null) return null;

        if (req.Title != null) task.Title = req.Title;
        if (req.Description != null) task.Description = req.Description;
        if (req.ProjectId.HasValue) task.ProjectId = req.ProjectId;
        if (req.TaskType.HasValue) task.TaskType = req.TaskType.Value;
        if (req.Weight.HasValue && req.Weight.Value > 0) task.Weight = req.Weight.Value;
        if (req.Priority.HasValue) task.Priority = req.Priority.Value;
        if (req.StartAt.HasValue) task.StartAt = req.StartAt.Value;
        if (req.DueAt.HasValue) task.DueAt = req.DueAt.Value;
        if (req.SortOrder.HasValue) task.SortOrder = req.SortOrder.Value;

        if (req.Status.HasValue)
            await SetStatusAsync(task, req.Status.Value);

        task.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        // Propagate completion up if needed
        if (task.ParentId.HasValue)
            await TryAutoCompleteAncestorsAsync(task.ParentId.Value, userId);

        return await GetByIdAsync(userId, taskId);
    }

    // Advances the next uncompleted child for sequential tasks,
    // or completes all children for parallel tasks.
    public async Task<TaskResponse?> TickAsync(Guid userId, Guid taskId)
    {
        var task = await db.Tasks
            .Include(t => t.Children)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.OwnerId == userId);

        if (task == null) return null;

        // Leaf task — complete directly
        if (task.Children.Count == 0)
        {
            await SetStatusAsync(task, ItemStatus.Completed);
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            if (task.ParentId.HasValue)
                await TryAutoCompleteAncestorsAsync(task.ParentId.Value, userId);

            return await GetByIdAsync(userId, taskId);
        }

        if (task.TaskType == TaskType.Sequential)
        {
            var next = task.Children
                .OrderBy(c => c.SortOrder)
                .FirstOrDefault(c => c.Status is not ItemStatus.Completed and not ItemStatus.Cancelled);

            if (next != null)
            {
                await LoadChildrenRecursiveAsync([next]);
                await CompleteRecursiveAsync(next);
                next.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
                await TryAutoCompleteAncestorsAsync(taskId, userId);
            }
        }
        else // Parallel
        {
            await LoadChildrenRecursiveAsync(task.Children.ToList());
            foreach (var child in task.Children)
                await CompleteRecursiveAsync(child);

            await db.SaveChangesAsync();
            await TryAutoCompleteAncestorsAsync(taskId, userId);
        }

        return await GetByIdAsync(userId, taskId);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid taskId)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.OwnerId == userId);
        if (task == null) return false;

        await LoadChildrenRecursiveAsync([task]);
        DeleteRecursive(task);
        await db.SaveChangesAsync();
        return true;
    }

    // --- Private helpers ---

    private async Task SetStatusAsync(TaskItem task, ItemStatus status)
    {
        task.Status = status;
        if (status == ItemStatus.Completed)
            task.CompletedAt = DateTimeOffset.UtcNow;
        else
            task.CompletedAt = null;

        if (status == ItemStatus.Cancelled)
        {
            await LoadChildrenRecursiveAsync([task]);
            CancelRecursive(task);
        }
    }

    private async Task CompleteRecursiveAsync(TaskItem task)
    {
        task.Status = ItemStatus.Completed;
        task.CompletedAt = DateTimeOffset.UtcNow;

        await LoadChildrenRecursiveAsync([task]);
        foreach (var child in task.Children)
            await CompleteRecursiveAsync(child);
    }

    private void CancelRecursive(TaskItem task)
    {
        task.Status = ItemStatus.Cancelled;
        task.CompletedAt = DateTimeOffset.UtcNow;
        foreach (var child in task.Children)
            CancelRecursive(child);
    }

    private void DeleteRecursive(TaskItem task)
    {
        foreach (var child in task.Children)
            DeleteRecursive(child);
        db.Tasks.Remove(task);
    }

    private async Task TryAutoCompleteAncestorsAsync(Guid parentId, Guid userId)
    {
        var parent = await db.Tasks
            .Include(t => t.Children)
            .FirstOrDefaultAsync(t => t.Id == parentId && t.OwnerId == userId);

        if (parent == null) return;

        bool allDone = parent.Children.Count > 0 &&
                       parent.Children.All(c => c.Status is ItemStatus.Completed or ItemStatus.Cancelled);

        if (allDone && parent.Status != ItemStatus.Completed)
        {
            parent.Status = ItemStatus.Completed;
            parent.CompletedAt = DateTimeOffset.UtcNow;
            parent.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            if (parent.ParentId.HasValue)
                await TryAutoCompleteAncestorsAsync(parent.ParentId.Value, userId);
        }
    }

    private async Task LoadChildrenRecursiveAsync(List<TaskItem> tasks)
    {
        if (tasks.Count == 0) return;

        var ids = tasks.Select(t => t.Id).ToList();
        var children = await db.Tasks
            .Where(t => t.ParentId.HasValue && ids.Contains(t.ParentId!.Value))
            .OrderBy(t => t.SortOrder)
            .ToListAsync();

        foreach (var task in tasks)
            task.Children = children.Where(c => c.ParentId == task.Id).ToList();

        await LoadChildrenRecursiveAsync(children);
    }

    public static TaskResponse MapToResponse(TaskItem task)
    {
        var progress = TaskProgressService.ComputeProgress(task);
        var (completed, total) = TaskProgressService.GetChildCounts(task);
        var effectivePriority = TaskProgressService.ComputeEffectivePriority(task);
        var nextTitle = TaskProgressService.GetNextChildTitle(task);

        return new TaskResponse(
            task.Id,
            task.OwnerId,
            task.ProjectId,
            task.ParentId,
            task.Title,
            task.Description,
            task.TaskType.ToString(),
            task.Weight,
            task.Priority.ToString(),
            effectivePriority,
            task.Status.ToString(),
            task.StartAt,
            task.DueAt,
            task.CompletedAt,
            task.SortOrder,
            progress,
            completed,
            total,
            nextTitle,
            task.CreatedAt,
            task.UpdatedAt,
            task.Children.OrderBy(c => c.SortOrder).Select(MapToResponse).ToList()
        );
    }
}
