from __future__ import annotations

import sqlite3
from datetime import datetime, timezone
from pathlib import Path

from app.models.schemas import RouteResponse


class RouteRepository:
    """Хранилище рассчитанных маршрутов в SQLite."""

    def __init__(self, database_path: Path) -> None:
        self._database_path = database_path

    def initialize(self) -> None:
        self._database_path.parent.mkdir(parents=True, exist_ok=True)
        with self._connect() as connection:
            connection.execute("PRAGMA journal_mode = WAL")
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS route_results (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    from_lon REAL NOT NULL,
                    from_lat REAL NOT NULL,
                    to_lon REAL NOT NULL,
                    to_lat REAL NOT NULL,
                    response_json TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    UNIQUE (from_lon, from_lat, to_lon, to_lat)
                )
                """
            )

    def find(
        self,
        from_lonlat: tuple[float, float],
        to_lonlat: tuple[float, float],
    ) -> RouteResponse | None:
        with self._connect() as connection:
            row = connection.execute(
                """
                SELECT response_json
                FROM route_results
                WHERE from_lon = ? AND from_lat = ?
                  AND to_lon = ? AND to_lat = ?
                LIMIT 1
                """,
                (*from_lonlat, *to_lonlat),
            ).fetchone()

        if row is None:
            return None

        route = RouteResponse.model_validate_json(row[0])
        return route.model_copy(update={"from_database": True}, deep=True)

    def save(
        self,
        from_lonlat: tuple[float, float],
        to_lonlat: tuple[float, float],
        route: RouteResponse,
    ) -> None:
        response_json = route.model_copy(
            update={"from_database": False},
            deep=True,
        ).model_dump_json()
        with self._connect() as connection:
            connection.execute(
                """
                INSERT INTO route_results (
                    from_lon, from_lat, to_lon, to_lat,
                    response_json, created_at_utc
                )
                VALUES (?, ?, ?, ?, ?, ?)
                ON CONFLICT (from_lon, from_lat, to_lon, to_lat) DO NOTHING
                """,
                (
                    *from_lonlat,
                    *to_lonlat,
                    response_json,
                    datetime.now(timezone.utc).isoformat(),
                ),
            )

    def count(self) -> int:
        with self._connect() as connection:
            return int(connection.execute("SELECT COUNT(*) FROM route_results").fetchone()[0])

    def _connect(self) -> sqlite3.Connection:
        return sqlite3.connect(self._database_path, timeout=30.0)
