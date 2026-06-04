using OpenPlan.API.Models;

namespace OpenPlan.API.Services;

public static class TaskProgressService
{
    public static double ComputeProgress(TaskItem task)
    {
        if (task.Children.Count == 0)
            return task.Status is ItemStatus.Completed or ItemStatus.Cancelled ? 1.0 : 0.0;

        var children = task.TaskType == TaskType.Sequential
            ? GetSequentialChildren(task.Children)
            : task.Children.ToList();

        double weightedSum = 0;
        double totalWeight = 0;

        foreach (var child in children)
        {
            totalWeight += child.Weight;
            weightedSum += child.Weight * ComputeProgress(child);
        }

        return totalWeight == 0 ? 0 : weightedSum / totalWeight;
    }

    // For sequential tasks, sub-tasks after the first uncompleted one contribute 0.
    // We return the list with those tasks zeroed out by only including up to and including
    // the first uncompleted child (the rest are excluded from progress).
    private static List<TaskItem> GetSequentialChildren(ICollection<TaskItem> children)
    {
        var ordered = children.OrderBy(c => c.SortOrder).ToList();
        var result = new List<TaskItem>();

        foreach (var child in ordered)
        {
            result.Add(child);
            if (child.Status is not ItemStatus.Completed and not ItemStatus.Cancelled)
                break;
        }

        // Remaining children after the first uncompleted one contribute weight but 0 completion,
        // so we must include them with status Scheduled to keep denominator correct.
        var firstUncompleted = ordered.FirstOrDefault(c =>
            c.Status is not ItemStatus.Completed and not ItemStatus.Cancelled);

        if (firstUncompleted != null)
        {
            var rest = ordered.SkipWhile(c => c.Id != firstUncompleted.Id).Skip(1).ToList();
            // These contribute 0 progress — represent them as scheduled leaf tasks for calculation
            foreach (var remaining in rest)
            {
                result.Add(new TaskItem
                {
                    Weight = remaining.Weight,
                    Status = ItemStatus.Scheduled,
                    Children = []
                });
            }
        }

        return result;
    }

    public static string ComputeEffectivePriority(TaskItem task)
    {
        var allPriorities = CollectUncompletedPriorities(task);
        if (allPriorities.Count == 0) return task.Priority.ToString();
        return allPriorities.Min().ToString();
    }

    private static List<TaskPriority> CollectUncompletedPriorities(TaskItem task)
    {
        var result = new List<TaskPriority>();
        if (task.Status is ItemStatus.Completed or ItemStatus.Cancelled) return result;

        result.Add(task.Priority);
        foreach (var child in task.Children)
            result.AddRange(CollectUncompletedPriorities(child));

        return result;
    }

    public static string? GetNextChildTitle(TaskItem task)
    {
        if (task.TaskType != TaskType.Sequential) return null;

        return task.Children
            .OrderBy(c => c.SortOrder)
            .FirstOrDefault(c => c.Status is not ItemStatus.Completed and not ItemStatus.Cancelled)
            ?.Title;
    }

    public static (int completed, int total) GetChildCounts(TaskItem task)
    {
        int total = task.Children.Count;
        int completed = task.Children.Count(c => c.Status is ItemStatus.Completed or ItemStatus.Cancelled);
        return (completed, total);
    }
}
