using GeoProcessingService.Core.Domain.Entities;
using GeoProcessingService.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoProcessingService.Core.Infrastructure.Persistence;

public sealed class ProcessingJobRepository(GeoProcessingDbContext dbContext)
    : IProcessingJobRepository
{
    public async Task AddAsync(ProcessingJob job, CancellationToken cancellationToken = default)
    {
        dbContext.ProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ProcessingJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProcessingJob>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.ProcessingJobs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
}
