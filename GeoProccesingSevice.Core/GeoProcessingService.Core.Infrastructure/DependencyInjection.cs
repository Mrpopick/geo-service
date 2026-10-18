using GeoProcessingService.Core.Domain.Repositories;
using GeoProcessingService.Core.Domain.Services;
using GeoProcessingService.Core.Infrastructure.Persistence;
using GeoProcessingService.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoProcessingService.Core.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("GeoProcessing")
            ?? "Data Source=data/geoprocessing.db";

        services.AddDbContext<GeoProcessingDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IProcessingJobRepository, ProcessingJobRepository>();
        services.AddScoped<IGeoProcessingService, Services.GeoProcessingService>();
        return services;
    }

    public static async Task InitializeInfrastructureAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GeoProcessingDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }
}
