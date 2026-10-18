import json
import os
import uuid
from datetime import datetime

import pandas as pd
from fastapi.responses import FileResponse

# Папка для готовых файлов (монтируется через Docker volume)
EXPORT_DIR = os.getenv("EXPORT_DIR", "exports")
os.makedirs(EXPORT_DIR, exist_ok=True)

# Колонки строго в нужном порядке
COLUMNS = [
    "time", "date",
    "from_lon", "from_lat",
    "to_lon", "to_lat",
    "total_length_m", "total_time_s",
]


def parse_routes(lines: list[str]) -> pd.DataFrame:
    """Превращает сырые NDJSON-строки в таблицу."""
    rows = []
    for line in lines:
        line = line.strip()
        if not line:
            continue
        try:
            obj = json.loads(line)
        except json.JSONDecodeError:
            continue  # битую строку пропускаем

        request = obj.get("request", {})
        response = obj.get("response", {})
        created_at = obj.get("created_at")

        # Время и дата из "2026-10-18T11:27:24.013707+00:00"
        time_str, date_str = None, None
        if created_at:
            dt = datetime.fromisoformat(created_at.replace("Z", "+00:00"))
            time_str = dt.strftime("%H:%M:%S")
            date_str = dt.strftime("%d.%m.%Y")

        frm = request.get("from")
        to = request.get("to")

        rows.append({
            "time": time_str,
            "date": date_str,
            "from_lon": frm[0] if frm else None,
            "from_lat": frm[1] if frm else None,
            "to_lon": to[0] if to else None,
            "to_lat": to[1] if to else None,
            "total_length_m": response.get("total_length_m"),
            "total_time_s": response.get("total_time_s"),
        })

    df = pd.DataFrame(rows, columns=COLUMNS)
    df["total_length_m"] = pd.to_numeric(df["total_length_m"], errors="coerce").round(2)
    df["total_time_s"] = pd.to_numeric(df["total_time_s"], errors="coerce").round(2)
    return df


def save_and_respond(lines: list[str], filename: str, fmt: str) -> FileResponse:
    """Парсит строки, сохраняет в файл, возвращает FileResponse."""
    df = parse_routes(lines)

    unique = uuid.uuid4().hex[:8]
    path = os.path.join(EXPORT_DIR, f"{filename}_{unique}.{fmt}")

    if fmt == "csv":
        df.to_csv(path, index=False, encoding="utf-8-sig")  # BOM для Excel
    elif fmt == "xlsx":
        df.to_excel(path, index=False, engine="openpyxl")
    else:
        raise ValueError(f"Unsupported format: {fmt}")

    media = (
        "text/csv" if fmt == "csv"
        else "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    )
    return FileResponse(path=path, filename=os.path.basename(path), media_type=media)