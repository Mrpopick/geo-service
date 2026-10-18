using GeoProcessingService.Core.Domain.Exceptions;
using GeoProcessingService.Core.Domain.Models;
using GeoProcessingService.Core.Domain.Services;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace GeoProcessingService.Core.Application.Controllers;

[ApiController]
[Route("api/v1/geoprocessing")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
public sealed class GeoProcessingController(
    IGeoProcessingService service,
    IWebHostEnvironment environment) : ControllerBase
{
    private const long MaximumFileSize = 25 * 1024 * 1024;

    [HttpPost("buffer")]
    [Consumes("multipart/form-data")]
    [Produces("application/geo+json")]
    public async Task<IActionResult> Buffer(
        IFormFile file,
        [FromForm] string distance,
        CancellationToken cancellationToken)
    {
        var result = await service.BufferAsync (
            await ReadGeoJsonAsync(file, cancellationToken),
            ParseNumber(distance, "distance"),
            cancellationToken);
        return GeoJson(result);
    }

    [HttpPost("intersection")]
    [Consumes("multipart/form-data")]
    [Produces("application/geo+json")]
    public async Task<IActionResult> Intersection(
        IFormFile firstFile,
        IFormFile secondFile,
        CancellationToken cancellationToken)
    {
        var first = await ReadGeoJsonAsync(firstFile, cancellationToken);
        var second = await ReadGeoJsonAsync(secondFile, cancellationToken);
        return GeoJson(await service.IntersectionAsync(first, second, cancellationToken));
    }

    [HttpPost("union")]
    [Consumes("multipart/form-data")]
    [Produces("application/geo+json")]
    public async Task<IActionResult> Union(
        IFormFile firstFile,
        IFormFile? secondFile,
        CancellationToken cancellationToken)
    {
        var first = await ReadGeoJsonAsync(firstFile, cancellationToken);
        var second = secondFile is null ? null : await ReadGeoJsonAsync(secondFile, cancellationToken);
        return GeoJson(await service.UnionAsync(first, second, cancellationToken));
    }

    [HttpPost("difference")]
    [Consumes("multipart/form-data")]
    [Produces("application/geo+json")]
    public async Task<IActionResult> Difference(
        IFormFile firstFile,
        IFormFile secondFile,
        CancellationToken cancellationToken)
    {
        var first = await ReadGeoJsonAsync(firstFile, cancellationToken);
        var second = await ReadGeoJsonAsync(secondFile, cancellationToken);
        return GeoJson(await service.DifferenceAsync(first, second, cancellationToken));
    }

    [HttpPost("clip")]
    [Consumes("multipart/form-data")]
    [Produces("application/geo+json")]
    public async Task<IActionResult> Clip(
        IFormFile sourceFile,
        IFormFile maskFile,
        CancellationToken cancellationToken)
    {
        var source = await ReadGeoJsonAsync(sourceFile, cancellationToken);
        var mask = await ReadGeoJsonAsync(maskFile, cancellationToken);
        return GeoJson(await service.ClipAsync(source, mask, cancellationToken));
    }

    [HttpPost("simplify")]
    [Consumes("multipart/form-data")]
    [Produces("application/geo+json")]
    public async Task<IActionResult> Simplify(
        IFormFile file,
        [FromForm] string tolerance,
        CancellationToken cancellationToken)
    {
        var input = await ReadGeoJsonAsync(file, cancellationToken);
        return GeoJson(await service.SimplifyAsync(input, ParseNumber(tolerance, "tolerance"), cancellationToken));
    }

    [HttpPost("centroid")]
    [Consumes("multipart/form-data")]
    [Produces("application/geo+json")]
    public async Task<IActionResult> Centroid(IFormFile file, CancellationToken cancellationToken) =>
        GeoJson(await service.CentroidAsync(await ReadGeoJsonAsync(file, cancellationToken), cancellationToken));

    [HttpPost("convex-hull")]
    [Consumes("multipart/form-data")]
    [Produces("application/geo+json")]
    public async Task<IActionResult> ConvexHull(IFormFile file, CancellationToken cancellationToken) =>
        GeoJson(await service.ConvexHullAsync(await ReadGeoJsonAsync(file, cancellationToken), cancellationToken));

    [HttpPost("validate")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    public async Task<ActionResult<ValidationResult>> Validate(
        IFormFile file,
        CancellationToken cancellationToken) =>
        Ok(await service.ValidateAsync(await ReadGeoJsonAsync(file, cancellationToken), cancellationToken));


    [HttpPost("graph")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    public async Task<ActionResult<GraphResult>> Graph(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var geoJson = await ReadGeoJsonAsync(file, cancellationToken);
        var result = await service.BuildGraphAsync(geoJson, cancellationToken);
        return GraphResponse(result);
    }

    [HttpPost("graph/default")]
    [Produces("application/json")]
    [ProducesResponseType<GraphResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GraphResult>> DefaultGraph(
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(
            environment.ContentRootPath,
            "SeedData",
            "road-network.geojson");
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException("The default road network GeoJSON was not found.", path);

        var geoJson = await System.IO.File.ReadAllTextAsync(path, cancellationToken);
        var result = await service.BuildGraphAsync(geoJson, cancellationToken);
        return GraphResponse(result);
    }

    private ActionResult<GraphResult> GraphResponse(GraphResult result)
    {
        var viewUrl = Url.RouteUrl(
            GraphViewController.ViewGraphRouteName,
            new { id = result.JobId },
            Request.Scheme);
        var downloadUrl = Url.RouteUrl(
            GraphViewController.DownloadGraphRouteName,
            new { id = result.JobId },
            Request.Scheme);
        if (viewUrl is not null)
            Response.Headers.Location = viewUrl;
        return Ok(result with
        {
            ViewUrl = viewUrl,
            DownloadUrl = downloadUrl
        });
    }

    private ContentResult GeoJson(GeoProcessingResult result)
    {
        Response.Headers["X-Processing-Job-Id"] = result.JobId.ToString();
        return Content(result.GeoJson, "application/geo+json");
    }

    private static async Task<string> ReadGeoJsonAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            throw new InvalidGeoJsonException("A non-empty GeoJSON file is required.");

        if (file.Length > MaximumFileSize)
            throw new InvalidGeoJsonException("The GeoJSON file exceeds the 25 MB limit.");

        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static double ParseNumber(string value, string fieldName)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new ArgumentException($"Form field '{fieldName}' must be a number in invariant format (for example, 0.25).");
        return result;
    }
}
