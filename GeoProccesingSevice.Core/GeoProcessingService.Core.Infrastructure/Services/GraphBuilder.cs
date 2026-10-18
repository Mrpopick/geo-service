using System.Text.Json;
using GeoProcessingService.Core.Domain.Exceptions;
using GeoProcessingService.Core.Domain.Models;
using System.Globalization;
using NetTopologySuite;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace GeoProcessingService.Core.Infrastructure.Services;

internal static class GraphBuilder
{
    public static GraphBuildData Build(IReadOnlyList<GeoJsonFeature> features)
    {
        var nodeIds = new Dictionary<(double X, double Y), int>();
        var coordinates = new List<Coordinate>();
        var rawEdges = new List<RawGraphEdge>();

        int GetNode(Coordinate coordinate)
        {
            var key = (Math.Round(coordinate.X, 9), Math.Round(coordinate.Y, 9));
            if (nodeIds.TryGetValue(key, out var existing))
                return existing;

            var id = coordinates.Count;
            nodeIds[key] = id;
            coordinates.Add(new Coordinate(key.Item1, key.Item2));
            return id;
        }

        foreach (var feature in features)
            AddGeometry(feature.Geometry, feature, GetNode, rawEdges);

        if (coordinates.Count == 0)
            throw new InvalidGeoJsonException("A graph requires Point, LineString, MultiLineString, or polygon coordinates.");

        var degree = new int[coordinates.Count];
        var edges = rawEdges.Select((edge, id) =>
        {
            degree[edge.Source]++;
            degree[edge.Target]++;
            return new GraphEdge(
                id,
                edge.Source,
                edge.Target,
                edge.Length,
                edge.RoadType,
                edge.MaxSpeed);
        }).ToList();
        var nodes = coordinates
            .Select((coordinate, id) => new GraphNode(id, coordinate.X, coordinate.Y, degree[id]))
            .ToList();

        var featureCollection = CreateGeoJson(nodes, edges, coordinates);
        using var json = JsonDocument.Parse(new GeoJsonWriter().Write(featureCollection));
        return new GraphBuildData(
            nodes.Count,
            edges.Count,
            nodes,
            edges,
            json.RootElement.Clone());
    }

    private static void AddGeometry(
        Geometry geometry,
        GeoJsonFeature feature,
        Func<Coordinate, int> getNode,
        List<RawGraphEdge> edges)
    {
        switch (geometry)
        {
            case Point point when !point.IsEmpty:
                getNode(point.Coordinate);
                break;
            case LineString lineString:
                AddLine(lineString, feature, getNode, edges);
                break;
            case GeometryCollection collection:
                for (var i = 0; i < collection.NumGeometries; i++)
                    AddGeometry(collection.GetGeometryN(i), feature, getNode, edges);
                break;
        }
    }

    private static void AddLine(
        LineString line,
        GeoJsonFeature feature,
        Func<Coordinate, int> getNode,
        List<RawGraphEdge> edges)
    {
        var roadType = GetAttribute(feature.Attributes, "highway");
        var maxSpeed = GetAttribute(feature.Attributes, "maxspeed");
        var points = line.Coordinates;
        if (points.Length == 1)
        {
            getNode(points[0]);
            return;
        }

        for (var i = 1; i < points.Length; i++)
        {
            var start = points[i - 1];
            var end = points[i];
            if (start.Equals2D(end))
                continue;

            var source = getNode(start);
            var target = getNode(end);
            var length = start.Distance(end);
            edges.Add(new RawGraphEdge(source, target, length, roadType, maxSpeed));
        }
    }

    private static string? GetAttribute(IAttributesTable attributes, string name)
    {
        if (!attributes.Exists(name) || attributes[name] is null)
            return null;

        var value = Convert.ToString(attributes[name], CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static FeatureCollection CreateGeoJson(
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyList<Coordinate> coordinates)
    {
        var factory = NtsGeometryServices.Instance.CreateGeometryFactory();
        var collection = new FeatureCollection();

        foreach (var node in nodes)
        {
            var attributes = new AttributesTable();
            attributes.Add("kind", "node");
            attributes.Add("id", node.Id);
            attributes.Add("degree", node.Degree);
            collection.Add(new Feature(factory.CreatePoint(coordinates[node.Id]), attributes));
        }

        foreach (var edge in edges)
        {
            var attributes = new AttributesTable();
            attributes.Add("kind", "edge");
            attributes.Add("id", edge.Id);
            attributes.Add("source", edge.Source);
            attributes.Add("target", edge.Target);
            attributes.Add("length", edge.Length);
            if (edge.RoadType is not null)
                attributes.Add("roadType", edge.RoadType);
            if (edge.MaxSpeed is not null)
                attributes.Add("maxSpeed", edge.MaxSpeed);
            var line = factory.CreateLineString(
                [new Coordinate(coordinates[edge.Source]), new Coordinate(coordinates[edge.Target])]);
            collection.Add(new Feature(line, attributes));
        }

        return collection;
    }
}

internal sealed record RawGraphEdge(
    int Source,
    int Target,
    double Length,
    string? RoadType,
    string? MaxSpeed);

internal sealed record GraphBuildData(
    int NodeCount,
    int EdgeCount,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    object GeoJson);
