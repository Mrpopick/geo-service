import os
from pathlib import Path


GEOPROCESSING_URL = os.getenv(
    "GEOPROCESSING_URL",
    "http://localhost:8080",
).rstrip("/")

DEFAULT_GRAPH_ENDPOINT = "/api/v1/geoprocessing/graph/default"

ROUTING_DB_PATH = Path(
    os.getenv(
        "ROUTING_DB_PATH",
        Path(__file__).resolve().parent.parent / "data" / "routing.db",
    )
)
