from pathlib import Path
import re
import json
import zipfile
import geopandas as gpd
from typing import cast
from shapely.geometry import Point, LineString

# Маппинг типов геометрии GeoPandas -> категории для SHP
GEOM_CATEGORIES = {
    "Point": "points",
    "MultiPoint": "points",
    "LineString": "lines",
    "MultiLineString": "lines",
    "Polygon": "polygons",
    "MultiPolygon": "polygons",
}

SHP_FIELD_MAX_LEN = 10

def parse_routes_jsonl(file_path: Path) -> gpd.GeoDataFrame:
    """Парсит JSONL с историей маршрутов от Routing Service."""
    features = []
    with file_path.open('r', encoding='utf-8') as f:
        for line in f:
            if not line.strip():
                continue
            data = json.loads(line)
            req = data.get('request', {})
            resp = data.get('response', {})
            
            # 1. Линия маршрута
            coords = resp.get('path_coords', [])
            if len(coords) >= 2:
                features.append({
                    'type': 'Feature',
                    'geometry': LineString(coords),
                    'properties': {
                        'length_m': resp.get('total_length_m'),
                        'time_s': resp.get('total_time_s'),
                        'created_at': data.get('created_at'),
                        'layer_type': 'route'
                    }
                })
            
            # 2. Точки старта и финиша
            for pt_type, pt_coord in [('start', req.get('from')), ('end', req.get('to'))]:
                if pt_coord and len(pt_coord) == 2:
                    features.append({
                        'type': 'Feature',
                        'geometry': Point(pt_coord),
                        'properties': {
                            'type': pt_type,
                            'created_at': data.get('created_at'),
                            'layer_type': 'endpoint'
                        }
                    })
                    
    if not features:
        raise ValueError("Не найдено валидных геометрий в JSONL")
        
    # Создаем GeoDataFrame. Система координат OSM по умолчанию - WGS84.
    return gpd.GeoDataFrame.from_features(features, crs="EPSG:4326")


def read_input(file_path: Path) -> gpd.GeoDataFrame:
    """Универсальная функция чтения: определяет формат по расширению."""
    if file_path.suffix.lower() == '.jsonl':
        gdf = parse_routes_jsonl(file_path)
    else:
        gdf = gpd.read_file(file_path)
        
    if gdf.crs is None:
        gdf = gdf.set_crs("EPSG:4326")
    if gdf.empty:
        raise ValueError("Входные данные не содержат объектов")
        
    return gdf

def _sanitize_columns(gdf: gpd.GeoDataFrame) -> gpd.GeoDataFrame:
    """Приводит имена колонок к валидному для DBF виду.

    DBF не принимает ':', обрезает имена до 10 символов и падает
    при дубликатах. Здесь мы последовательно решаем все три проблемы.
    """
    new_columns: dict[str, str] = {}
    used: set[str] = set()

    for col in gdf.columns:
        if col == "geometry":
            continue
        # 1. Заменяем всё не-буквенно-цифровое на '_'
        clean = re.sub(r"[^A-Za-z0-9_]", "_", col)
        # 2. Обрезаем до лимита
        clean = clean[:SHP_FIELD_MAX_LEN]
        # 3. Разрешаем коллизии: addr_street_1, addr_street_2 и т.д.
        base = clean
        counter = 1
        while clean in used:
            suffix = str(counter)
            clean = base[: SHP_FIELD_MAX_LEN - len(suffix)] + suffix
            counter += 1
        used.add(clean)
        if clean != col:
            new_columns[col] = clean

    return gdf.rename(columns=new_columns)

def convert_to_shp(input_path: Path, output_path: Path) -> None:
    """Конвертирует GeoJSON в ZIP-архив с набором Shapefile'ов.

    Разделяем данные на три файла (points/lines/polygons), потому что
    Shapefile не поддерживает смешанные геометрии в одном файле.
    """
    gdf = read_input(input_path)
    if gdf.crs is None:
        gdf = gdf.set_crs("EPSG:4326")
    if gdf.empty:
        raise ValueError("GeoJSON не содержит объектов")

    # Определяем категорию для каждого объекта
    gdf["_geom_category"] = gdf.geometry.geom_type.map(GEOM_CATEGORIES)

    unsupported = gdf["_geom_category"].isna()
    if unsupported.any():
        bad = gdf.loc[unsupported, "geometry"].geom_type.unique().tolist()
        raise ValueError(f"Unsupported geometry types: {bad}")

    # Создаем временную папку рядом с будущим архивом
    shp_dir = output_path.parent / "shp_files"
    shp_dir.mkdir(parents=True, exist_ok=True)

    written: list[Path] = []
    for category, group in gdf.groupby("_geom_category"):
        group = cast(gpd.GeoDataFrame, group)
        group = group.drop(columns=["_geom_category"]).copy()
        group = _sanitize_columns(group)

        shp_file = shp_dir / f"{category}.shp"
        # encoding='utf-8' критичен для кириллицы: он создаёт .cpg
        # файл с записью 'UTF-8', иначе QGIS покажет кракозябры.
        group.to_file(shp_file, driver="ESRI Shapefile", encoding="utf-8")
        written.append(shp_file)

    # Упаковываем все sidecar-файлы в один ZIP
    with zipfile.ZipFile(output_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for shp_file in written:
            for ext in (".shp", ".shx", ".dbf", ".prj", ".cpg"):
                side_file = shp_file.with_suffix(ext)
                if side_file.exists():
                    zf.write(side_file, arcname=side_file.name)

def convert_to_gpkg(input_path: Path, output_path: Path) -> None:
    """Читает GeoJSON и сохраняет как GeoPackage."""
    gdf = read_input(input_path)
    if gdf.empty:
        raise ValueError("GeoJSON не содержит объектов")
    gdf.to_file(output_path, driver="GPKG", layer="data")

def convert_to_kml(input_path: Path, output_path: Path) -> None:
    """Конвертирует GeoJSON в KML.
    
    Группирует объекты в папки (Folders) по типам геометрии.
    """
    gdf = read_input(input_path)
    if gdf.crs is None:
        gdf = gdf.set_crs("EPSG:4326")
    if gdf.empty:
        raise ValueError("GeoJSON не содержит объектов")

    # KML требует WGS84
    gdf = gdf.to_crs("EPSG:4326")

    gdf["_geom_category"] = gdf.geometry.geom_type.map(GEOM_CATEGORIES)

    unsupported = gdf["_geom_category"].isna()
    if unsupported.any():
        bad = gdf.loc[unsupported, "geometry"].geom_type.unique().tolist()
        raise ValueError(f"Unsupported geometry types: {bad}")

    first = True
    for category, group in gdf.groupby("_geom_category"):
        group = cast(gpd.GeoDataFrame, group)
        group = group.drop(columns=["_geom_category"]).copy()

        # Для KML используем драйвер LIBKML (он есть в вашем списке)
        mode = "w" if first else "a"
        group.to_file(
            output_path,
            driver="LIBKML",
            layer=category,
            mode=mode,
        )
        first = False


# Реестр экспортеров: добавляем новый формат — просто регистрируем функцию
EXPORTERS = {
    "gpkg": convert_to_gpkg,
    "shp": convert_to_shp,
    "kml": convert_to_kml,
}