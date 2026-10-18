from fastapi import APIRouter, HTTPException
from pydantic import BaseModel
from app.services.routes_parser import save_and_respond

router = APIRouter()


class RoutesExportRequest(BaseModel):
    """Тело запроса: сырые строки NDJSON от Routing Service."""
    lines: list[str]
    filename: str = "routes"


@router.post("/export/routes/csv")
async def export_routes_csv(request: RoutesExportRequest):
    if not request.lines:
        raise HTTPException(400, "lines is empty")
    try:
        return save_and_respond(request.lines, request.filename, "csv")
    except Exception as e:
        raise HTTPException(500, detail=str(e))


@router.post("/export/routes/xlsx")
async def export_routes_xlsx(request: RoutesExportRequest):
    if not request.lines:
        raise HTTPException(400, "lines is empty")
    try:
        return save_and_respond(request.lines, request.filename, "xlsx")
    except Exception as e:
        raise HTTPException(500, detail=str(e))