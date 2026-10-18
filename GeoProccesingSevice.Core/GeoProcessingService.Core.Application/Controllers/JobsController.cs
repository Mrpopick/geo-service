using GeoProcessingService.Core.Domain.Entities;
using GeoProcessingService.Core.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace GeoProcessingService.Core.Application.Controllers;

[ApiController]
[Route("api/v1/geoprocessing/jobs")]
public sealed class JobsController(IProcessingJobRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetRecent([FromQuery] int take = 20, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var jobs = await repository.GetRecentAsync(take, cancellationToken);
        return Ok(jobs.Select(job => new
        {
            job.Id,
            job.Operation,
            job.IsSuccessful,
            job.ErrorMessage,
            job.CreatedAtUtc,
            job.DurationMilliseconds
        }));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProcessingJob>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var job = await repository.GetByIdAsync(id, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }
}
