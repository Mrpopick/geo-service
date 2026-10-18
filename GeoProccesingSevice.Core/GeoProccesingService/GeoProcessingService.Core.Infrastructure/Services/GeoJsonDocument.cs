using System.Text.Json;
using GeoProcessingService.Core.Domain.Exceptions;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace GeoProcessingService.Core.Infrastructure.Services;

internal static class GeoJsonDocument
{
    public static IReadOnlyList<GeoJsonFeature> ReadFeatures(string geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
            throw new InvalidGeoJsonException("GeoJSON file is empty.");

        try
        {
            using var json = JsonDocument.Parse(geoJson);
            if (!json.RootElement.TryGetProperty("type", out var typeElement))
                throw new InvalidGeoJsonException("The JSON document has no GeoJSON 'type' property.");

            var reader = new GeoJsonReader();
            var type = typeElement.GetString();
            var features = type switch
            {
                "FeatureCollection" => reader.Read<FeatureCollection>(geoJson)
                    .Select((feature, index) => new { feature, index })
                    .Where(item => item.feature.Geometry is not null)
                    .Select(item => new GeoJsonFeature(
                        item.feature.Geometry,
                        item.feature.Attributes ?? new AttributesTable(),
                        item.index))
                    .ToList(),
                "Feature" => ReadFeature(reader, geoJson),
                _ =>
                [
                    new GeoJsonFeature(reader.Read<Geometry>(geoJson), new AttributesTable(), 0)
                ]
            };

            if (features.Count == 0)
                throw new InvalidGeoJsonException("GeoJSON contains no geometries.");

            return features;
        }
        catch (InvalidGeoJsonException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ParseException or ArgumentException)
        {
            throw new InvalidGeoJsonException($"Cannot parse GeoJSON: {exception.Message}", exception);
        }
    }

    public static IReadOnlyList<Geometry> ReadGeometries(string geoJson) =>
        ReadFeatures(geoJson).Select(feature => feature.Geometry).ToList();

    public static Geometry Combine(IReadOnlyList<Geometry> geometries)
    {
        var factory = geometries[0].Factory;
        return factory.BuildGeometry(geometries.ToList());
    }

    public static string WriteFeatureCollection(
        IEnumerable<GeoJsonFeature> features,
        string operation)
    {
        var collection = new FeatureCollection();
        foreach (var feature in features)
        {
            var attributes = CopyAttributes(feature.Attributes);
            SetAttribute(attributes, "operation", operation);
            if (feature.SourceIndex is not null)
                SetAttribute(attributes, "sourceIndex", feature.SourceIndex.Value);
            if (feature.SecondaryIndex is not null)
                SetAttribute(attributes, "secondaryIndex", feature.SecondaryIndex.Value);
            collection.Add(new Feature(feature.Geometry, attributes));
        }

        return new GeoJsonWriter().Write(collection);
    }

    private static List<GeoJsonFeature> ReadFeature(GeoJsonReader reader, string geoJson)
    {
        var feature = reader.Read<Feature>(geoJson);
        return feature.Geometry is null
            ? []
            : [new GeoJsonFeature(feature.Geometry, feature.Attributes ?? new AttributesTable(), 0)];
    }

    private static AttributesTable CopyAttributes(IAttributesTable source)
    {
        var copy = new AttributesTable();
        foreach (var name in source.GetNames())
            copy.Add(name, source[name]);
        return copy;
    }

    private static void SetAttribute(AttributesTable attributes, string name, object value)
    {
        if (attributes.Exists(name))
            attributes[name] = value;
        else
            attributes.Add(name, value);
    }
}

internal sealed record GeoJsonFeature(
    Geometry Geometry,
    IAttributesTable Attributes,
    int? SourceIndex,
    int? SecondaryIndex = null);
