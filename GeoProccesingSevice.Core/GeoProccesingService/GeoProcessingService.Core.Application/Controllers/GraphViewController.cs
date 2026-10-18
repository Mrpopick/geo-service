using GeoProcessingService.Core.Application.Views;
using GeoProcessingService.Core.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace GeoProcessingService.Core.Application.Controllers;

[ApiController]
[Route("api/v1/geoprocessing/graph")]
public sealed class GraphViewController(IProcessingJobRepository repository) : ControllerBase
{
    public const string ViewGraphRouteName = "ViewGraph";
    public const string DownloadGraphRouteName = "DownloadGraph";

    [HttpGet("{id:guid}/view", Name = ViewGraphRouteName)]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ViewGraph(Guid id, CancellationToken cancellationToken)
    {
        var job = await repository.GetByIdAsync(id, cancellationToken);
        if (job is null || !string.Equals(job.Operation, "graph", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        if (!job.IsSuccessful)
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Graph construction failed",
                detail: job.ErrorMessage);

        return Content(GraphHtmlRenderer.Render(id, job.OutputJson), "text/html; charset=utf-8");
    }

    [HttpGet("{id:guid}/download", Name = DownloadGraphRouteName)]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadGraph(Guid id, CancellationToken cancellationToken)
    {
        var job = await repository.GetByIdAsync(id, cancellationToken);
        if (job is null || !string.Equals(job.Operation, "graph", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        if (!job.IsSuccessful)
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Graph construction failed",
                detail: job.ErrorMessage);

        using var graph = JsonDocument.Parse(job.OutputJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("jobId", id);
            foreach (var property in graph.RootElement.EnumerateObject())
                property.WriteTo(writer);
            writer.WriteEndObject();
        }

        return File(
            stream.ToArray(),
            "application/json; charset=utf-8",
            $"graph-{id}.json");
    }
}
