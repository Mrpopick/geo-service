from __future__ import annotations

import json
from datetime import datetime, timezone
from threading import Lock

from app.core.config import ROUTE_HISTORY_PATH


_history_lock = Lock()


def append_route(
    from_: tuple[float, float],
    to: tuple[float, float],
    response: dict,
) -> None:
    ROUTE_HISTORY_PATH.parent.mkdir(parents=True, exist_ok=True)
    record = {
        "request": {"from": list(from_), "to": list(to)},
        "response": response,
        "created_at": datetime.now(timezone.utc).isoformat(),
    }
    with _history_lock:
        with ROUTE_HISTORY_PATH.open("a", encoding="utf-8") as file:
            file.write(json.dumps(record, ensure_ascii=False) + "\n")


def read_route_history() -> list[str]:
    """Возвращает непустые строки истории для сервисов экспорта."""
    if not ROUTE_HISTORY_PATH.exists():
        return []

    with _history_lock:
        return [
            line
            for line in ROUTE_HISTORY_PATH.read_text(encoding="utf-8").splitlines()
            if line.strip()
        ]
