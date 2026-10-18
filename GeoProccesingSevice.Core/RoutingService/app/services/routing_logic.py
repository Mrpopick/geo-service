from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Any
from uuid import UUID

import networkx as nx

from app.models.schemas import GraphEdge, GraphResult, RoutePoint, RouteResponse

# --- Настройки автомобильного роутинга ---

DEFAULT_SPEED_KMH = 50.0
MAX_SNAP_DISTANCE_M = 2_000.0

# Скорости по умолчанию для типов дорог (если maxSpeed не указан)
HIGHWAY_SPEED_KMH: dict[str, float] = {
    "motorway": 110.0,
    "trunk": 90.0,
    "primary": 70.0,
    "secondary": 60.0,
    "tertiary": 50.0,
    "unclassified": 40.0,
    "residential": 30.0,
    "living_street": 20.0,
    "service": 20.0,  # Дворовые проезды / парковки
    "secondary_link": 60.0,
    "tertiary_link": 50.0,


    "footway": 5.0,
    "cycleway": 15.0,
    "path": 5.0,
    "steps": 2.0,
}

# Типы дорог, по которым машины ездить не могут (исключаем из графа)
FORBIDDEN_ROAD_TYPES = {
    "footway",
    "cycleway",
    "steps",
    "path",
    "pedestrian",
    "track"
}


def edge_speed_kmh(edge: GraphEdge) -> float:
    """Вычисляет скорость для ребра (км/ч)."""
    # 1. Если есть явное ограничение maxSpeed от OSM — используем его
    if edge.max_speed is not None and edge.max_speed > 0:
        return edge.max_speed

    # 2. Иначе берем по типу дороги
    road_type = edge.road_type or edge.highway
    if road_type is not None and road_type in HIGHWAY_SPEED_KMH:
        return HIGHWAY_SPEED_KMH[road_type]

    # 3. Иначе дефолт
    return DEFAULT_SPEED_KMH


def edge_is_oneway(edge: GraphEdge) -> bool:
    return bool(edge.oneway)


# --- Геометрия ---

def haversine_m(lon1: float, lat1: float, lon2: float, lat2: float) -> float:
    r = 6371000.0
    phi1, phi2 = math.radians(lat1), math.radians(lat2)
    dphi = math.radians(lat2 - lat1)
    dlambda = math.radians(lon2 - lon1)
    a = (
            math.sin(dphi / 2) ** 2
            + math.cos(phi1) * math.cos(phi2) * math.sin(dlambda / 2) ** 2
    )
    return 2 * r * math.asin(math.sqrt(a))


# --- Загрузка ---

def load_graph_from_file(path: str | Path) -> tuple[nx.Graph, GraphResult]:
    raw = json.loads(Path(path).read_text(encoding="utf-8-sig"))  # используем utf-8-sig на всякий случай
    return load_graph_from_data(raw)


def load_graph_from_data(raw: dict[str, Any]) -> tuple[nx.Graph, GraphResult]:
    result = GraphResult.model_validate(raw)

    # Проверяем, есть ли хоть одно oneway (на будущее)
    has_oneway = any(e.oneway for e in result.edges)
    g: nx.Graph = nx.DiGraph() if has_oneway else nx.Graph()

    for node in result.nodes:
        g.add_node(node.id, x=node.x, y=node.y, degree=node.degree)

    skipped_endpoints = 0
    skipped_forbidden = 0

    for edge in result.edges:
        if edge.source not in g or edge.target not in g:
            skipped_endpoints += 1
            continue

        # ИСКЛЮЧАЕМ пешеходные и велосипедные дорожки
        road_type = edge.road_type or edge.highway
        if road_type in FORBIDDEN_ROAD_TYPES:
            skipped_forbidden += 1
            continue

        a = g.nodes[edge.source]
        b = g.nodes[edge.target]
        length_m = haversine_m(a["x"], a["y"], b["x"], b["y"])

        speed_kmh = edge_speed_kmh(edge)
        speed_mps = speed_kmh * 1000.0 / 3600.0
        time_s = length_m / speed_mps if speed_mps > 0 else float("inf")

        # ВНИМАНИЕ: теперь weight = time_s (кратчайшее ВРЕМЯ)!
        attrs = dict(
            id=edge.id,
            length_deg=edge.length,
            length_m=length_m,
            speed_kmh=speed_kmh,
            time_s=time_s,
            weight=time_s,  # <--- ИЗМЕНЕНО ЗДЕСЬ (было length_m)
            road_type=road_type,
            oneway=bool(edge.oneway),
        )

        if g.is_directed() and edge_is_oneway(edge):
            g.add_edge(edge.source, edge.target, **attrs)
        else:
            g.add_edge(edge.source, edge.target, **attrs)
            if g.is_directed():
                g.add_edge(edge.target, edge.source, **attrs)

    if skipped_endpoints:
        print(f"[load] skipped {skipped_endpoints} edges (missing endpoints)")
    if skipped_forbidden:
        print(f"[load] skipped {skipped_forbidden} edges (forbidden road types)")

    print(f"[load] Graph built: {g.number_of_nodes()} nodes, {g.number_of_edges()} edges")

    return g, result


# --- Ближайший узел ---

def find_nearest_node(graph: nx.Graph, lon: float, lat: float) -> tuple[int, float]:
    best_id, best_dist = None, float("inf")
    for node_id, data in graph.nodes(data=True):
        if graph.degree(node_id) == 0:
            continue
        d = haversine_m(lon, lat, data["x"], data["y"])
        if d < best_dist:
            best_dist, best_id = d, node_id
    if best_id is None:
        raise ValueError("No suitable node found (graph empty or all isolated)")
    return best_id, best_dist


# --- Роутинг ---

def build_route(
        graph: nx.Graph,
        from_lonlat: tuple[float, float],
        to_lonlat: tuple[float, float],
        graph_job_id: UUID,
) -> RouteResponse:
    from_lon, from_lat = from_lonlat
    to_lon, to_lat = to_lonlat

    src, from_snap_distance = find_nearest_node(graph, from_lon, from_lat)
    dst, to_snap_distance = find_nearest_node(graph, to_lon, to_lat)

    if from_snap_distance > MAX_SNAP_DISTANCE_M:
        raise ValueError(
            f"Начальная точка находится вне покрытия дорожного графа: "
            f"до ближайшей дороги {from_snap_distance:.0f} м "
            f"(допустимо не более {MAX_SNAP_DISTANCE_M:.0f} м)."
        )
    if to_snap_distance > MAX_SNAP_DISTANCE_M:
        raise ValueError(
            f"Конечная точка находится вне покрытия дорожного графа: "
            f"до ближайшей дороги {to_snap_distance:.0f} м "
            f"(допустимо не более {MAX_SNAP_DISTANCE_M:.0f} м)."
        )

    # Ищем по весу (weight), который теперь равен time_s
    try:
        path = nx.shortest_path(graph, source=src, target=dst, weight="weight")
    except nx.NetworkXNoPath:
        raise ValueError(f"No path between nodes {src} and {dst}")

    coords: list[tuple[float, float]] = []
    for node_id in path:
        data = graph.nodes[node_id]
        coords.append((data["x"], data["y"]))

    total_length_m = 0.0
    total_time_s = 0.0
    for u, v in zip(path[:-1], path[1:]):
        e = graph[u][v]
        total_length_m += e["length_m"]
        total_time_s += e["time_s"]

    return RouteResponse(
        graph_job_id=graph_job_id,
        requested_from=from_lonlat,
        requested_to=to_lonlat,
        from_node=RoutePoint(
            node_id=src,
            x=graph.nodes[src]["x"],
            y=graph.nodes[src]["y"],
            snap_distance_m=from_snap_distance,
        ),
        to_node=RoutePoint(
            node_id=dst,
            x=graph.nodes[dst]["x"],
            y=graph.nodes[dst]["y"],
            snap_distance_m=to_snap_distance,
        ),
        path_node_ids=path,
        path_coords=coords,
        total_length_m=total_length_m,
        total_time_s=total_time_s,
    )
