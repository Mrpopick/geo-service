namespace GeoProcessingService.Core.Domain.Exceptions;

public sealed class InvalidGeoJsonException(string message, Exception? innerException = null)
    : Exception(message, innerException);
