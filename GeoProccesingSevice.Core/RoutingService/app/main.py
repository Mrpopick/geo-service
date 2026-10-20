from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException
from pydantic import ValidationError

from app.api.v1.exports import router as exports_router
from app.core.config import ROUTING_DB_PATH
from app.models.schemas import RouteRequest, RouteResponse
from app.services.geoprocessing_client import (
    GeoProcessingServiceError,
    fetch_default_graph,
)
from app.services.routing_logic import build_route, load_graph_from_data
from app.services.export_log import append_route
from app.services.route_repository import RouteRepository

route_repository = RouteRepository(ROUTING_DB_PATH)


@asynccontextmanager
async def lifespan(_: FastAPI):
    route_repository.initialize()
    yield

app = FastAPI(
    title="Routing Service",
    description="Сервис построения маршрутов (автомобильные дороги, кратчайшее время)",
    version="0.1.0",
    lifespan=lifespan,
)
app.include_router(exports_router)


@app.get("/")
def read_root():
    return {"status": "ok", "service": "routing-service"}


@app.get("/health")
def health_check():
    return {
        "status": "healthy",
        "graphSource": "geo-processing-service",
        "storedRoutes": route_repository.count(),
    }


@app.post("/api/v1/routes/shortest-time", response_model=RouteResponse)
def shortest_time(req: RouteRequest) -> RouteResponse:
    stored = route_repository.find(req.from_, req.to)
    if stored is not None:
        append_route(req.from_, req.to, stored.model_dump(mode="json"))
        return stored

    try:
        graph_payload = fetch_default_graph()
        graph, graph_result = load_graph_from_data(graph_payload)
        result = build_route(graph, req.from_, req.to, graph_result.job_id)
    except GeoProcessingServiceError as error:
        raise HTTPException(status_code=503, detail=str(error)) from error
    except ValidationError as error:
        raise HTTPException(
            status_code=502,
            detail=f"GeoProcessing Service returned an invalid graph: {error}",
        ) from error
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e)) from e

    route_repository.save(req.from_, req.to, result)
    append_route(req.from_, req.to, result.model_dump(mode="json"))
    return result

