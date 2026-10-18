using GeoProcessingService.Core.Domain.Exceptions;
using GeoProcessingService.Core.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using NetTopologySuite.Geometries;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    options.MultipartBodyLengthLimit = 50 * 1024 * 1024);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "data"));
await app.Services.InitializeInfrastructureAsync();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = exception switch
    {
        InvalidGeoJsonException => (StatusCodes.Status400BadRequest, "Invalid GeoJSON"),
        ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request parameters"),
        TopologyException => (StatusCodes.Status422UnprocessableEntity, "Geometry operation failed"),
        _ => (StatusCodes.Status500InternalServerError, "Internal server error")
    };

    var detail = status == StatusCodes.Status500InternalServerError && !app.Environment.IsDevelopment()
        ? "An unexpected error occurred."
        : exception?.Message;
    await Results.Problem(statusCode: status, title: title, detail: detail).ExecuteAsync(context);
}));

app.UseSwagger();
app.UseSwaggerUI();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();
app.Run();

public partial class Program;
