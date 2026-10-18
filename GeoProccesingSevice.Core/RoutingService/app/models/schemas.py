from __future__ import annotations

from typing import Any, Optional
from uuid import UUID

from pydantic import AliasChoices, BaseModel, ConfigDict, Field


class GraphNode(BaseModel):
    model_config = ConfigDict(populate_by_name=True)
    id: int
    x: float
    y: float
    degree: int = 0


class GraphEdge(BaseModel):
    model_config = ConfigDict(populate_by_name=True)
    id: int
    source: int
    target: int
    length: float

    road_type: Optional[str] = Field(default=None, validation_alias=AliasChoices("roadType", "road_type"))
    max_speed: Optional[float] = Field(default=None, validation_alias=AliasChoices("maxSpeed", "max_speed"))

    highway: Optional[str] = None
    oneway: Optional[bool] = None
    speed_kmh: Optional[float] = None


class GraphResult(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    job_id: UUID = Field(validation_alias=AliasChoices("jobId", "JobId"))
    node_count: int = Field(validation_alias=AliasChoices("nodeCount", "NodeCount"))
    edge_count: int = Field(validation_alias=AliasChoices("edgeCount", "EdgeCount"))
    nodes: list[GraphNode]
    edges: list[GraphEdge]
    geo_json: Any = Field(default=None, validation_alias=AliasChoices("geoJson", "GeoJson"))


class RouteRequest(BaseModel):
    model_config = ConfigDict(populate_by_name=True)
    from_: tuple[float, float] = Field(
        ...,
        alias="from",
        description="Начальная точка [долгота, широта]",
    )
    to: tuple[float, float] = Field(
        ...,
        alias="to",
        description="Конечная точка [долгота, широта]",
    )


class RoutePoint(BaseModel):
    node_id: int
    x: float
    y: float
    snap_distance_m: float


class RouteResponse(BaseModel):
    graph_job_id: UUID
    from_database: bool = False
    requested_from: tuple[float, float]
    requested_to: tuple[float, float]
    from_node: RoutePoint
    to_node: RoutePoint
    path_node_ids: list[int]
    path_coords: list[tuple[float, float]]
    total_length_m: float
    total_time_s: float
    units: dict[str, str] = {"length": "meters", "time": "seconds"}
