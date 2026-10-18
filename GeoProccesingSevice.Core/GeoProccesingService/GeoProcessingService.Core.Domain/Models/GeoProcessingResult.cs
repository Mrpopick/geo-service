namespace GeoProcessingService.Core.Domain.Models;

public sealed record GeoProcessingResult(Guid JobId, string GeoJson);

public sealed record ValidationItem(int FeatureIndex, bool IsValid, string? Error);

public sealed record ValidationResult(
    Guid JobId,
    bool IsValid,
    int GeometryCount,
    IReadOnlyList<ValidationItem> Geometries);

public sealed record GraphNode(int Id, double X, double Y, int Degree);

public sealed record GraphEdge(
    int Id,
    int Source,
    int Target,
    double Length,
    string? RoadType,
    string? MaxSpeed);

public sealed record GraphResult(
    Guid JobId,
    int NodeCount,
    int EdgeCount,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    object GeoJson,
    string? ViewUrl = null,
    string? DownloadUrl = null);
