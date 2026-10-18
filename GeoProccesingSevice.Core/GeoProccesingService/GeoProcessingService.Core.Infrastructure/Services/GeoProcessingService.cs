using System.Diagnostics;
using System.Text.Json;
using GeoProcessingService.Core.Domain.Entities;
using GeoProcessingService.Core.Domain.Exceptions;
using GeoProcessingService.Core.Domain.Models;
using GeoProcessingService.Core.Domain.Repositories;
using GeoProcessingService.Core.Domain.Services;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Operation.Valid;
using NetTopologySuite.Simplify;

namespace GeoProcessingService.Core.Infrastructure.Services;

public sealed class GeoProcessingService(IProcessingJobRepository repository) : IGeoProcessingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<GeoProcessingResult> BufferAsync(
        string geoJson,
        double distance,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(distance))
            throw new ArgumentOutOfRangeException(nameof(distance), "Distance must be finite.");

        return ProcessGeometryAsync(
            "buffer",
            geoJson,
            null,
            JsonSerializer.Serialize(new { distance }, JsonOptions),
            geometry => geometry.Buffer(distance),
            cancellationToken);
    }

    public Task<GeoProcessingResult> IntersectionAsync(
        string first,
        string second,
        CancellationToken cancellationToken) =>
        ProcessBinaryAsync("intersection", first, second, (a, b) => a.Intersection(b), cancellationToken);

    public Task<GeoProcessingResult> UnionAsync(
        string first,
        string? second,
        CancellationToken cancellationToken) =>
        ProcessUnionAsync(first, second, cancellationToken);

    public Task<GeoProcessingResult> DifferenceAsync(
        string first,
        string second,
        CancellationToken cancellationToken) =>
        ProcessBinaryAsync("difference", first, second, (a, b) => a.Difference(b), cancellationToken);

    public Task<GeoProcessingResult> ClipAsync(
        string source,
        string mask,
        CancellationToken cancellationToken) =>
        ProcessBinaryAsync("clip", source, mask, (a, b) => a.Intersection(b), cancellationToken);

    public Task<GeoProcessingResult> SimplifyAsync(
        string geoJson,
        double tolerance,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance must be finite and non-negative.");

        return ProcessGeometryAsync(
            "simplify",
            geoJson,
            null,
            JsonSerializer.Serialize(new { tolerance }, JsonOptions),
            geometry => TopologyPreservingSimplifier.Simplify(geometry, tolerance),
            cancellationToken);
    }

    public Task<GeoProcessingResult> CentroidAsync(
        string geoJson,
        CancellationToken cancellationToken) =>
        ProcessGeometryAsync("centroid", geoJson, null, null, geometry => geometry.Centroid, cancellationToken);

    public Task<GeoProcessingResult> ConvexHullAsync(
        string geoJson,
        CancellationToken cancellationToken) =>
        ProcessGeometryAsync("convex-hull", geoJson, null, null, geometry => geometry.ConvexHull(), cancellationToken);

    public async Task<ValidationResult> ValidateAsync(string geoJson, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var geometries = GeoJsonDocument.ReadGeometries(geoJson);
        var items = geometries.Select((geometry, index) =>
        {
            var validation = new IsValidOp(geometry);
            var error = validation.ValidationError;
            return new ValidationItem(index, error is null, error?.Message);
        }).ToList();

        var payload = new
        {
            isValid = items.All(x => x.IsValid),
            geometryCount = items.Count,
            geometries = items
        };
        var output = JsonSerializer.Serialize(payload, JsonOptions);
        var job = await SaveJobAsync("validate", geoJson, null, output, null, true, null, stopwatch, cancellationToken);
        return new ValidationResult(job.Id, payload.isValid, items.Count, items);
    }

    public async Task<GraphResult> BuildGraphAsync(string geoJson, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var features = GeoJsonDocument.ReadFeatures(geoJson);
        var lineFeatures = features
            .Where(feature => feature.Geometry is LineString or MultiLineString)
            .ToList();
        if (lineFeatures.Count == 0)
            throw new InvalidGeoJsonException("A graph requires LineString or MultiLineString features.");

        // OSM exports can contain buildings and POIs together with roads. If the
        // highway tag is present, only actual roads participate in the graph.
        var roadFeatures = lineFeatures.Any(feature => feature.Attributes.Exists("highway"))
            ? lineFeatures.Where(feature => feature.Attributes.Exists("highway")).ToList()
            : lineFeatures;

        // Node the whole network, then map the split linework back to every source
        // feature so its highway and maxspeed attributes are retained.
        var network = UnaryUnionOp.Union(
            GeoJsonDocument.Combine(roadFeatures.Select(feature => feature.Geometry).ToList()));
        var nodedRoadFeatures = roadFeatures
            .Select(feature => feature with { Geometry = feature.Geometry.Intersection(network) })
            .Where(feature => !feature.Geometry.IsEmpty)
            .ToList();
        var graph = GraphBuilder.Build(nodedRoadFeatures);
        var outputWithoutId = JsonSerializer.Serialize(graph, JsonOptions);
        var parameters = JsonSerializer.Serialize(
            new { inputFeatureCount = features.Count, roadFeatureCount = roadFeatures.Count },
            JsonOptions);
        var job = await SaveJobAsync(
            "graph", geoJson, null, outputWithoutId, parameters, true, null, stopwatch, cancellationToken);
        return new GraphResult(job.Id, graph.NodeCount, graph.EdgeCount, graph.Nodes, graph.Edges, graph.GeoJson);
    }

    private async Task<GeoProcessingResult> ProcessBinaryAsync(
        string operation,
        string first,
        string second,
        Func<Geometry, Geometry, Geometry> transform,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var firstFeatures = GeoJsonDocument.ReadFeatures(first);
            var secondGeometries = GeoJsonDocument.ReadGeometries(second);
            var overlayGeometry = UnaryUnionOp.Union(GeoJsonDocument.Combine(secondGeometries));
            var resultFeatures = firstFeatures
                .Select(feature => feature with { Geometry = transform(feature.Geometry, overlayGeometry) })
                .Where(feature => !feature.Geometry.IsEmpty)
                .ToList();
            var output = GeoJsonDocument.WriteFeatureCollection(resultFeatures, operation);
            var parameters = JsonSerializer.Serialize(
                new { sourceFeatureCount = firstFeatures.Count, overlayFeatureCount = secondGeometries.Count },
                JsonOptions);
            var job = await SaveJobAsync(
                operation, first, second, output, parameters, true, null, stopwatch, cancellationToken);
            return new GeoProcessingResult(job.Id, output);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await SaveJobAsync(
                operation, first, second, "{}", null, false, exception.Message, stopwatch, cancellationToken);
            throw;
        }
    }

    private async Task<GeoProcessingResult> ProcessGeometryAsync(
        string operation,
        string input,
        string? secondaryInput,
        string? parameters,
        Func<Geometry, Geometry> transform,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var inputFeatures = GeoJsonDocument.ReadFeatures(input);
            var resultFeatures = inputFeatures
                .Select(feature => feature with { Geometry = transform(feature.Geometry) })
                .Where(feature => !feature.Geometry.IsEmpty)
                .ToList();
            var output = GeoJsonDocument.WriteFeatureCollection(resultFeatures, operation);
            var job = await SaveJobAsync(
                operation, input, secondaryInput, output, parameters, true, null, stopwatch, cancellationToken);
            return new GeoProcessingResult(job.Id, output);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await SaveJobAsync(
                operation, input, secondaryInput, "{}", parameters, false, exception.Message, stopwatch, cancellationToken);
            throw;
        }
    }

    private async Task<GeoProcessingResult> ProcessUnionAsync(
        string first,
        string? second,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var geometries = GeoJsonDocument.ReadGeometries(first).ToList();
            if (second is not null)
                geometries.AddRange(GeoJsonDocument.ReadGeometries(second));

            var union = UnaryUnionOp.Union(GeoJsonDocument.Combine(geometries));
            var outputFeature = new GeoJsonFeature(union, new NetTopologySuite.Features.AttributesTable(), null);
            var output = GeoJsonDocument.WriteFeatureCollection([outputFeature], "union");
            var job = await SaveJobAsync(
                "union", first, second, output, null, true, null, stopwatch, cancellationToken);
            return new GeoProcessingResult(job.Id, output);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await SaveJobAsync(
                "union", first, second, "{}", null, false, exception.Message, stopwatch, cancellationToken);
            throw;
        }
    }

    private async Task<ProcessingJob> SaveJobAsync(
        string operation,
        string input,
        string? secondaryInput,
        string output,
        string? parameters,
        bool isSuccessful,
        string? error,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        stopwatch.Stop();
        var job = new ProcessingJob
        {
            Operation = operation,
            InputGeoJson = input,
            SecondaryInputGeoJson = secondaryInput,
            OutputJson = output,
            ParametersJson = parameters,
            IsSuccessful = isSuccessful,
            ErrorMessage = error,
            DurationMilliseconds = stopwatch.ElapsedMilliseconds
        };
        await repository.AddAsync(job, cancellationToken);
        return job;
    }
}
