using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenPlan.API.DTOs.Projects;
using OpenPlan.API.Services;

namespace OpenPlan.API.Controllers;

[ApiController]
[Route("api/v1/projects")]
[Authorize]
public class ProjectsController(ProjectService projects) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetProjects()
    {
        return Ok(await projects.GetAllAsync(UserId));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetProject(Guid id)
    {
        var project = await projects.GetByIdAsync(UserId, id);
        return project == null ? NotFound() : Ok(project);
    }

    [HttpPost]
    public async Task<IActionResult> CreateProject(CreateProjectRequest req)
    {
        var project = await projects.CreateAsync(UserId, req);
        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, project);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProject(Guid id, UpdateProjectRequest req)
    {
        var project = await projects.UpdateAsync(UserId, id, req);
        return project == null ? NotFound() : Ok(project);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProject(Guid id)
    {
        var deleted = await projects.DeleteAsync(UserId, id);
        return deleted ? NoContent() : NotFound();
    }
}
