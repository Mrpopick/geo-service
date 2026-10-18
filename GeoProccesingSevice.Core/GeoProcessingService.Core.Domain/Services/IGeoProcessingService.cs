using GeoProcessingService.Core.Domain.Models;

namespace GeoProcessingService.Core.Domain.Services;

public interface IGeoProcessingService
{
    Task<GeoProcessingResult> BufferAsync(string geoJson, double distance, CancellationToken cancellationToken);
    Task<GeoProcessingResult> IntersectionAsync(string first, string second, CancellationToken cancellationToken);
    Task<GeoProcessingResult> UnionAsync(string first, string? second, CancellationToken cancellationToken);
    Task<GeoProcessingResult> DifferenceAsync(string first, string second, CancellationToken cancellationToken);
    Task<GeoProcessingResult> ClipAsync(string source, string mask, CancellationToken cancellationToken);
    Task<GeoProcessingResult> SimplifyAsync(string geoJson, double tolerance, CancellationToken cancellationToken);
    Task<GeoProcessingResult> CentroidAsync(string geoJson, CancellationToken cancellationToken);
    Task<GeoProcessingResult> ConvexHullAsync(string geoJson, CancellationToken cancellationToken);
    Task<ValidationResult> ValidateAsync(string geoJson, CancellationToken cancellationToken);
    Task<GraphResult> BuildGraphAsync(string geoJson, CancellationToken cancellationToken);
}
