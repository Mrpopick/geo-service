namespace GeoProcessingService.Core.Domain.Entities;

public sealed class ProcessingJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Operation { get; set; }
    public required string InputGeoJson { get; set; }
    public string? SecondaryInputGeoJson { get; set; }
    public required string OutputJson { get; set; }
    public string? ParametersJson { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public long DurationMilliseconds { get; set; }
}
