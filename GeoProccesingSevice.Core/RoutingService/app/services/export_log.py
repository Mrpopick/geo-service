from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path

EXPORT_PATH = Path(__file__).resolve().parent.parent / "data" / "route_history.jsonl"


def append_route(
    from_: tuple[float, float],
    to: tuple[float, float],
    response: dict,
) -> None:
    EXPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    record = {
        "request": {"from": list(from_), "to": list(to)},
        "response": response,
        "created_at": datetime.now(timezone.utc).isoformat(),
    }
    with EXPORT_PATH.open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")