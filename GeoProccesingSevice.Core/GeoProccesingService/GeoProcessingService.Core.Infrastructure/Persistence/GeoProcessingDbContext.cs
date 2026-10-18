using GeoProcessingService.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoProcessingService.Core.Infrastructure.Persistence;

public sealed class GeoProcessingDbContext(DbContextOptions<GeoProcessingDbContext> options)
    : DbContext(options)
{
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<ProcessingJob>();
        job.ToTable("processing_jobs");
        job.HasKey(x => x.Id);
        job.Property(x => x.Operation).HasMaxLength(64).IsRequired();
        job.Property(x => x.InputGeoJson).IsRequired();
        job.Property(x => x.OutputJson).IsRequired();
        job.HasIndex(x => x.CreatedAtUtc);
        job.HasIndex(x => x.Operation);
    }
}
