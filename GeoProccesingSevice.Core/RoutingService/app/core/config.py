import os
from pathlib import Path


GEOPROCESSING_URL = os.getenv(
    "GEOPROCESSING_URL",
    "http://localhost:8080",
).rstrip("/")

SPATIAL_EXPORT_URL = os.getenv(
    "SPATIAL_EXPORT_URL",
    "http://localhost:8002",
).rstrip("/")

TABLE_EXPORT_URL = os.getenv(
    "TABLE_EXPORT_URL",
    "http://localhost:8003",
).rstrip("/")

DEFAULT_GRAPH_ENDPOINT = "/api/v1/geoprocessing/graph/default"

ROUTING_DB_PATH = Path(
    os.getenv(
        "ROUTING_DB_PATH",
        Path(__file__).resolve().parent.parent / "data" / "routing.db",
    )
)

ROUTE_HISTORY_PATH = Path(
    os.getenv(
        "ROUTE_HISTORY_PATH",
        Path(__file__).resolve().parent.parent / "data" / "route_history.jsonl",
    )
)
