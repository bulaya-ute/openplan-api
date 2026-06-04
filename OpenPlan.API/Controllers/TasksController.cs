using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenPlan.API.DTOs.Tasks;
using OpenPlan.API.Services;

namespace OpenPlan.API.Controllers;

[ApiController]
[Route("api/v1/tasks")]
[Authorize]
public class TasksController(TaskService tasks) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetTasks([FromQuery] string view = "inbox")
    {
        var result = await tasks.GetRootTasksAsync(UserId, view.ToLower());
        return Ok(result);
    }

    [HttpGet("project/{projectId}")]
    public async Task<IActionResult> GetProjectTasks(Guid projectId)
    {
        var result = await tasks.GetProjectTasksAsync(UserId, projectId);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTask(Guid id)
    {
        var task = await tasks.GetByIdAsync(UserId, id);
        return task == null ? NotFound() : Ok(task);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTask(CreateTaskRequest req)
    {
        var task = await tasks.CreateAsync(UserId, req);
        return CreatedAtAction(nameof(GetTask), new { id = task!.Id }, task);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTask(Guid id, UpdateTaskRequest req)
    {
        var task = await tasks.UpdateAsync(UserId, id, req);
        return task == null ? NotFound() : Ok(task);
    }

    [HttpPost("{id}/tick")]
    public async Task<IActionResult> TickTask(Guid id)
    {
        var task = await tasks.TickAsync(UserId, id);
        return task == null ? NotFound() : Ok(task);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTask(Guid id)
    {
        var deleted = await tasks.DeleteAsync(UserId, id);
        return deleted ? NoContent() : NotFound();
    }
}
