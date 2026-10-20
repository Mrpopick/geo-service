from enum import Enum
from typing import Optional
from pydantic import BaseModel


class TaskStatus(str, Enum):
    PENDING = "pending"
    PROCESSING = "processing"
    COMPLETED = "completed"
    FAILED = "failed"


class ExportFormat(str, Enum):
    GPKG = "gpkg"
    SHP = "shp"
    KML = "kml"


class TaskInfo(BaseModel):
    task_id: str
    format: ExportFormat
    status: TaskStatus
    created_at: str
    error: Optional[str] = None
    download_url: Optional[str] = None