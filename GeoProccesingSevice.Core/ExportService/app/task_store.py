from datetime import datetime, timezone
from typing import Dict, Optional

from .models import TaskInfo, TaskStatus, ExportFormat


class TaskStore:
    """Простое in-memory хранилище задач.
    
    В production'е заменить на Redis или таблицу в БД.
    """
    def __init__(self) -> None:
        self._tasks: Dict[str, TaskInfo] = {}

    def create(self, task_id: str, fmt: ExportFormat) -> TaskInfo:
        task = TaskInfo(
            task_id=task_id,
            format=fmt,
            status=TaskStatus.PENDING,
            created_at=datetime.now(timezone.utc).isoformat(),
        )
        self._tasks[task_id] = task
        return task

    def get(self, task_id: str) -> Optional[TaskInfo]:
        return self._tasks.get(task_id)

    def update_status(
        self,
        task_id: str,
        status: TaskStatus,
        error: Optional[str] = None,
    ) -> None:
        task = self._tasks.get(task_id)
        if task is None:
            return
        task.status = status
        if error is not None:
            task.error = error
        if status == TaskStatus.COMPLETED:
            task.download_url = f"/api/v1/spatial-export/{task_id}/download"


task_store = TaskStore()