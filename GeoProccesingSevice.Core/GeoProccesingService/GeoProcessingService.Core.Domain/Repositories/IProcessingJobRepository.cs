using GeoProcessingService.Core.Domain.Entities;

namespace GeoProcessingService.Core.Domain.Repositories;

public interface IProcessingJobRepository
{
    Task AddAsync(ProcessingJob job, CancellationToken cancellationToken = default);
    Task<ProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProcessingJob>> GetRecentAsync(int take, CancellationToken cancellationToken = default);
}
