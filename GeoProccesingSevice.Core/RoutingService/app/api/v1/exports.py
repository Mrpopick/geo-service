from enum import Enum

from fastapi import APIRouter, HTTPException, Query, Response, status

from app.services.export_clients import (
    ExportServiceError,
    create_spatial_export,
    download_spatial_export,
    export_route_table,
    get_spatial_export_status,
)
from app.services.export_log import read_route_history


class SpatialExportFormat(str, Enum):
    GPKG = "gpkg"
    SHP = "shp"
    KML = "kml"


class TableExportFormat(str, Enum):
    CSV = "csv"
    XLSX = "xlsx"


router = APIRouter(prefix="/api/v1/routes/export", tags=["Route export"])


def _history_or_404() -> list[str]:
    lines = read_route_history()
    if not lines:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Route history is empty. Build at least one route first.",
        )
    return lines


def _as_download(exported_file) -> Response:
    return Response(
        content=exported_file.content,
        media_type=exported_file.media_type,
        headers={"Content-Disposition": exported_file.content_disposition},
    )


def _gateway_error(error: ExportServiceError) -> HTTPException:
    upstream_status = error.status_code
    response_status = (
        upstream_status
        if upstream_status is not None and 400 <= upstream_status < 500
        else status.HTTP_502_BAD_GATEWAY
    )
    return HTTPException(status_code=response_status, detail=str(error))


@router.post("/spatial/{fmt}", status_code=status.HTTP_202_ACCEPTED)
def start_spatial_export(fmt: SpatialExportFormat):
    """Передает всю историю маршрутов в Spatial Export Service."""
    lines = _history_or_404()
    try:
        result = create_spatial_export(fmt.value, ("\n".join(lines) + "\n").encode("utf-8"))
    except ExportServiceError as error:
        raise _gateway_error(error) from error

    task_id = result.get("task_id")
    if not task_id:
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail="Spatial Export Service returned a response without task_id.",
        )
    return {
        "task_id": task_id,
        "status": result.get("status", "pending"),
        "status_url": f"/api/v1/routes/export/spatial/{task_id}/status",
        "download_url": f"/api/v1/routes/export/spatial/{task_id}/download",
    }


@router.get("/spatial/{task_id}/status")
def spatial_export_status(task_id: str):
    try:
        result = get_spatial_export_status(task_id)
    except ExportServiceError as error:
        raise _gateway_error(error) from error

    if result.get("download_url"):
        result["download_url"] = f"/api/v1/routes/export/spatial/{task_id}/download"
    return result


@router.get("/spatial/{task_id}/download")
def spatial_export_download(task_id: str) -> Response:
    try:
        return _as_download(download_spatial_export(task_id))
    except ExportServiceError as error:
        raise _gateway_error(error) from error


@router.post("/table/{fmt}")
def table_export(
    fmt: TableExportFormat,
    filename: str = Query(
        default="routes",
        min_length=1,
        max_length=100,
        pattern=r"^[\w-]+$",
    ),
) -> Response:
    """Передает всю историю маршрутов в Table Export Service и скачивает файл."""
    lines = _history_or_404()
    try:
        return _as_download(export_route_table(fmt.value, lines, filename))
    except ExportServiceError as error:
        raise _gateway_error(error) from error
