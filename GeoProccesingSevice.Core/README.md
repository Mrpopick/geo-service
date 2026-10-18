# GeoProcessing Service

REST API геообработки GeoJSON на C# / ASP.NET Core 10 с использованием
`NetTopologySuite`. Каждая операция и её результат сохраняются в SQLite.

## Архитектура

- `GeoProcessingService.Core.Domain` — ядро: доменная сущность, DTO результата и
  интерфейсы-порты геосервиса и репозитория. Не зависит от других проектов.
- `GeoProcessingService.Core.Infrastructure` — сервисы геообработки и построения графа,
  `NetTopologySuite`, `EF Core`, SQLite, контекст и репозиторий.
- `GeoProcessingService.Core.Application` — только HTTP-слой: REST-контроллеры,
  конфигурация приложения и обработка ошибок.

Application использует интерфейсы Domain, Infrastructure реализует эти интерфейсы,
а Domain не зависит от фреймворков хранения, NetTopologySuite или Web API. Ссылку
Application на Infrastructure использует только точка композиции `Program.cs` для DI.

## Запуск

### Оба сервиса через Docker Compose

В репозитории находятся два контейнера: `geo-processing-service` на .NET и
`routing-service` на Python/FastAPI. Собрать и запустить их одной командой:

```bash
docker compose up --build
```

После запуска доступны:

- Swagger геообработки: `http://localhost:8080/swagger`;
- Swagger маршрутизации: `http://localhost:8001/docs`;
- проверка геообработки: `http://localhost:8080/health`;
- проверка маршрутизации: `http://localhost:8001/health`.

Docker не опрашивает эти адреса автоматически: постоянный health-check отключён.
Ручки `/health` предназначены только для ручной проверки.

Контейнеры находятся в общей сети `backend`. Python обращается к .NET по адресу
из `GEOPROCESSING_URL=http://geo-processing-service:8080`. Для будущих вызовов
из .NET в Python задан параметр конфигурации
`Services:RoutingService:BaseUrl=http://routing-service:8000`. Внутри контейнеров
нельзя использовать `localhost` для обращения к соседнему сервису.

SQLite геосервиса хранится в Docker volume `geo-processing-data`, а данные графа
и история маршрутов Python — в `RoutingService/app/data` на компьютере.

Остановить сервисы:

```bash
docker compose down
```

Порты можно изменить, скопировав `.env.example` в `.env`.

### Только C# локально, без Docker

```powershell
dotnet restore GeoProcessingService.Core.slnx --configfile NuGet.Config
dotnet run --project GeoProcessingService.Core.Application
```

При первом запуске автоматически создаётся файл
`GeoProcessingService.Core.Application/data/geoprocessing.db`. Проверка состояния:

```text
GET http://localhost:5116/health
```

В Development OpenAPI доступен по адресу `/openapi/v1.json`. Готовые примеры
запросов находятся в `GeoProcessingService.Core.Application/GeoProcessingService.Core.Application.http`,
тестовые данные — в каталоге `examples`.

## API

Все входные файлы передаются как `multipart/form-data` и могут содержать GeoJSON
типа `Geometry`, `Feature` или `FeatureCollection`. Максимальный размер одного файла — 25 МБ.

Операции `buffer`, `simplify`, `centroid` и `convex-hull` выполняются отдельно для
каждого `Feature`: точка, линия и полигон не склеиваются, исходные `properties`
сохраняются. В результат дополнительно записываются `operation` и `sourceIndex`.
Для `intersection`, `difference` и `clip` каждый объект первого файла обрабатывается
отдельно относительно геометрии второго файла. `union` является исключением: по
смыслу операции он объединяет все переданные объекты в одну геометрию.

| Метод | Путь | Поля формы | Результат |
|---|---|---|---|
| POST | `/api/v1/geoprocessing/buffer` | `file`, `distance` | GeoJSON |
| POST | `/api/v1/geoprocessing/intersection` | `firstFile`, `secondFile` | GeoJSON |
| POST | `/api/v1/geoprocessing/union` | `firstFile`, необязательный `secondFile` | GeoJSON |
| POST | `/api/v1/geoprocessing/difference` | `firstFile`, `secondFile` | GeoJSON |
| POST | `/api/v1/geoprocessing/clip` | `sourceFile`, `maskFile` | GeoJSON |
| POST | `/api/v1/geoprocessing/simplify` | `file`, `tolerance` | GeoJSON |
| POST | `/api/v1/geoprocessing/centroid` | `file` | GeoJSON |
| POST | `/api/v1/geoprocessing/convex-hull` | `file` | GeoJSON |
| POST | `/api/v1/geoprocessing/validate` | `file` | отчёт JSON |
| POST | `/api/v1/geoprocessing/graph` | `file` | граф JSON + GeoJSON |
| POST | `/api/v1/geoprocessing/graph/default` | — | новый граф из встроенного `SeedData/road-network.geojson` |
| GET | `/api/v1/geoprocessing/graph/{id}/view` | — | интерактивная HTML-визуализация графа |
| GET | `/api/v1/geoprocessing/graph/{id}/download` | — | скачивание результата графа как `.json` |
| GET | `/api/v1/geoprocessing/jobs?take=20` | — | история операций |
| GET | `/api/v1/geoprocessing/jobs/{id}` | — | полная запись операции |

Пример:

```bash
curl -F "file=@examples/polygon.geojson" -F "distance=0.25" \
  http://localhost:5116/api/v1/geoprocessing/buffer
```

У GeoJSON-ответов идентификатор сохранённой операции передаётся в заголовке
`X-Processing-Job-Id`.

## Как строится граф

`POST /graph` принимает точечную, линейную или полигональную геометрию. Линии
сначала разбиваются (`noding`) в местах пересечений. Уникальные координаты становятся
вершинами, соседние координаты — рёбрами. Ответ содержит:

- `nodes`: `id`, координаты и степень вершины;
- `edges`: `source`, `target` и длину;
- `edges[].roadType`: значение OSM-тега `highway`, например `residential` или `service`;
- `edges[].maxSpeed`: исходное значение OSM-тега `maxspeed`, если оно задано;
- `geoJson`: вершины как `Point`, рёбра как `LineString` для визуализации.

Ответ также содержит `viewUrl` и `downloadUrl`. Откройте `viewUrl` в браузере, чтобы просматривать граф,
масштабировать и перемещать схему, фильтровать типы дорог и видеть свойства ребра
по клику. По `downloadUrl` скачивается готовый JSON-файл, который можно передать
другому человеку. Кнопка скачивания также есть в просмотрщике. Адрес просмотра
возвращается и в HTTP-заголовке `Location`.

`POST http://localhost:8001/api/v1/routes/shortest-time` автоматически вызывает
C#-ручку `/graph/default`, получает актуальный граф и только после этого строит
маршрут. Python больше не зависит от локального `mock_graph.json`. В ответе
`graph_job_id` показывает идентификатор операции построения графа в C#-сервисе.
Граф строится заново для каждого запроса маршрута. Точки дальше 2 км от ближайшей
дороги отклоняются с HTTP 400 вместо неявной привязки к краю графа.

Готовый маршрут сохраняется Python-сервисом в SQLite `RoutingService/app/data/routing.db`
по точной паре `from`/`to`. Повторный запрос тех же координат читается из базы,
не вызывает C# и возвращается с `from_database: true`. Для новой пары координат
граф снова запрашивается и строится, а ответ содержит `from_database: false`.
Записи сохраняются после перезапуска контейнеров.

Если в загруженном OSM GeoJSON присутствуют объекты с тегом `highway`, граф строится
только по ним: здания, POI и прочая геометрия не становятся рёбрами дорожной сети.

Расстояния, допуски и длины вычисляются в единицах исходной системы координат.
Для метрических значений входной GeoJSON должен быть заранее переведён в подходящую
проекцию; автоматическая трансформация CRS не выполняется.

## Хранение

В таблице `processing_jobs` сохраняются операция, оба исходных GeoJSON (если есть),
параметры, результат, успешность, ошибка, время создания и длительность. Строку
подключения можно заменить через `ConnectionStrings__GeoProcessing`.
