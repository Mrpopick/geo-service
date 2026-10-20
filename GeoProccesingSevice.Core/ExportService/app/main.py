import os
import shutil
import uuid
from pathlib import Path

from fastapi import BackgroundTasks, FastAPI, File, HTTPException, UploadFile
from fastapi.responses import FileResponse

from .exporters import EXPORTERS
from .models import ExportFormat, TaskStatus
from .task_store import task_store

app = FastAPI(title="Spatial Export Service")

EXPORT_DIR = Path(os.getenv("EXPORT_DIR", "./exports"))
EXPORT_DIR.mkdir(parents=True, exist_ok=True)

OUTPUT_EXTENSIONS = {
    "gpkg": ".gpkg",
    "shp": ".zip",      # shapefile всегда отдаём архивом
    "kml": ".kml",
}

MEDIA_TYPES = {
    "gpkg": "application/geopackage+sqlite3",
    "shp": "application/zip",
    "kml": "application/vnd.google-earth.kml+xml",
}


def run_export(task_id: str, fmt: str, input_path: Path, output_path: Path) -> None:
    """Фоновая задача: конвертация файла.
    
    FastAPI запускает эту функцию после того, как HTTP-ответ уже ушел клиенту.
    """
    task_store.update_status(task_id, TaskStatus.PROCESSING)
    try:
        EXPORTERS[fmt](input_path, output_path)
        task_store.update_status(task_id, TaskStatus.COMPLETED)
    except Exception as exc:
        task_store.update_status(task_id, TaskStatus.FAILED, error=str(exc))


@app.post("/api/v1/spatial-export/{fmt}", status_code=202)
async def create_export_task(
    fmt: ExportFormat,
    background_tasks: BackgroundTasks,
    file: UploadFile = File(...),
):
    """Создает задачу экспорта и сразу возвращает task_id.
    
    Код 202 Accepted означает: 'задача принята, но еще не выполнена'.
    """
    task_id = str(uuid.uuid4())
    task_dir = EXPORT_DIR / task_id
    task_dir.mkdir(parents=True, exist_ok=True)

    orig_ext = Path(file.filename).suffix.lower() if file.filename else ".geojson"
    if orig_ext not in (".jsonl", ".geojson", ".json"):
        orig_ext = ".geojson"  # fallback
        
    input_path = task_dir / f"input{orig_ext}"
    output_path = task_dir / f"output{OUTPUT_EXTENSIONS[fmt.value]}"

    # Сохраняем загруженный GeoJSON на диск
    with input_path.open("wb") as f:
        shutil.copyfileobj(file.file, f)

    # Регистрируем задачу
    task_store.create(task_id, fmt)

    # Ставим конвертацию в фон
    background_tasks.add_task(run_export, task_id, fmt.value, input_path, output_path)

    return {
        "task_id": task_id,
        "status": TaskStatus.PENDING.value,
        "status_url": f"/api/v1/spatial-export/{task_id}/status",
    }


@app.get("/api/v1/spatial-export/{task_id}/status")
async def get_task_status(task_id: str):
    """Возвращает текущий статус задачи экспорта."""
    task = task_store.get(task_id)
    if task is None:
        raise HTTPException(status_code=404, detail="Task not found")
    return task


@app.get("/api/v1/spatial-export/{task_id}/download")
async def download_result(task_id: str):
    """Отдает готовый файл. Возвращает 409, если задача еще не готова."""
    task = task_store.get(task_id)
    if task is None:
        raise HTTPException(status_code=404, detail="Task not found")
    if task.status != TaskStatus.COMPLETED:
        raise HTTPException(
            status_code=409,
            detail=f"Task is not ready. Status: {task.status.value}",
        )

    ext = OUTPUT_EXTENSIONS[task.format.value]
    output_path = EXPORT_DIR / task_id / f"output{ext}"
    if not output_path.exists():
        raise HTTPException(status_code=500, detail="Output file not found")

    return FileResponse(
        path=output_path,
        filename=f"export_{task_id}{ext}",
        media_type=MEDIA_TYPES[task.format.value],
    )