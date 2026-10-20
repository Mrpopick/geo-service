from __future__ import annotations

from dataclasses import dataclass
from typing import Any, NoReturn

import httpx

from app.core.config import SPATIAL_EXPORT_URL, TABLE_EXPORT_URL


class ExportServiceError(RuntimeError):
    def __init__(self, message: str, status_code: int | None = None) -> None:
        super().__init__(message)
        self.status_code = status_code


@dataclass(frozen=True)
class ExportedFile:
    content: bytes
    media_type: str
    content_disposition: str


def _raise_upstream_error(service_name: str, error: Exception) -> NoReturn:
    if isinstance(error, httpx.TimeoutException):
        raise ExportServiceError(f"{service_name} did not respond before the timeout.") from error
    if isinstance(error, httpx.HTTPStatusError):
        raise ExportServiceError(
            f"{service_name} returned HTTP {error.response.status_code}: "
            f"{error.response.text[:500]}",
            status_code=error.response.status_code,
        ) from error
    raise ExportServiceError(f"Could not call {service_name}: {error}") from error


def _file_from_response(response: httpx.Response) -> ExportedFile:
    return ExportedFile(
        content=response.content,
        media_type=response.headers.get("content-type", "application/octet-stream"),
        content_disposition=response.headers.get(
            "content-disposition",
            'attachment; filename="export.bin"',
        ),
    )


def create_spatial_export(fmt: str, history: bytes) -> dict[str, Any]:
    timeout = httpx.Timeout(60.0, connect=10.0)
    try:
        with httpx.Client(base_url=SPATIAL_EXPORT_URL, timeout=timeout) as client:
            response = client.post(
                f"/api/v1/spatial-export/{fmt}",
                files={"file": ("route_history.jsonl", history, "application/x-ndjson")},
            )
            response.raise_for_status()
            return response.json()
    except (httpx.HTTPError, ValueError) as error:
        _raise_upstream_error("Spatial Export Service", error)


def get_spatial_export_status(task_id: str) -> dict[str, Any]:
    try:
        with httpx.Client(base_url=SPATIAL_EXPORT_URL, timeout=15.0) as client:
            response = client.get(f"/api/v1/spatial-export/{task_id}/status")
            response.raise_for_status()
            return response.json()
    except (httpx.HTTPError, ValueError) as error:
        _raise_upstream_error("Spatial Export Service", error)


def download_spatial_export(task_id: str) -> ExportedFile:
    try:
        with httpx.Client(base_url=SPATIAL_EXPORT_URL, timeout=120.0) as client:
            response = client.get(f"/api/v1/spatial-export/{task_id}/download")
            response.raise_for_status()
            return _file_from_response(response)
    except httpx.HTTPError as error:
        _raise_upstream_error("Spatial Export Service", error)


def export_route_table(fmt: str, lines: list[str], filename: str) -> ExportedFile:
    timeout = httpx.Timeout(120.0, connect=10.0)
    try:
        with httpx.Client(base_url=TABLE_EXPORT_URL, timeout=timeout) as client:
            response = client.post(
                f"/api/v1/export/routes/{fmt}",
                json={"lines": lines, "filename": filename},
            )
            response.raise_for_status()
            return _file_from_response(response)
    except httpx.HTTPError as error:
        _raise_upstream_error("Table Export Service", error)
