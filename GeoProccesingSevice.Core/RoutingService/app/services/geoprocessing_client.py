from __future__ import annotations

from typing import Any

import httpx

from app.core.config import DEFAULT_GRAPH_ENDPOINT, GEOPROCESSING_URL


class GeoProcessingServiceError(RuntimeError):
    pass


def fetch_default_graph() -> dict[str, Any]:
    """Получает дорожный граф, построенный C#-сервисом из его исходного GeoJSON."""
    timeout = httpx.Timeout(300.0, connect=10.0)
    try:
        with httpx.Client(base_url=GEOPROCESSING_URL, timeout=timeout) as client:
            response = client.post(DEFAULT_GRAPH_ENDPOINT)
            response.raise_for_status()
            return response.json()
    except httpx.TimeoutException as error:
        raise GeoProcessingServiceError(
            "GeoProcessing Service did not return the graph before the timeout."
        ) from error
    except httpx.HTTPStatusError as error:
        raise GeoProcessingServiceError(
            f"GeoProcessing Service returned HTTP {error.response.status_code}: "
            f"{error.response.text[:500]}"
        ) from error
    except (httpx.RequestError, ValueError) as error:
        raise GeoProcessingServiceError(
            f"Could not receive the graph from GeoProcessing Service: {error}"
        ) from error
