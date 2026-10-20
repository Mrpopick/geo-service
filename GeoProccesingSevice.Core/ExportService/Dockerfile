# Базовый образ: slim-версия Debian + Python 3.11
# slim — потому что полный образ в 10 раз больше
FROM python:3.11-slim-bookworm

# Переменные окружения Python:
#   PYTHONDONTWRITEBYTECODE — не создавать .pyc файлы внутри контейнера
#   PYTHONUNBUFFERED — логи сразу идут в stdout/stderr, а не буферизуются
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

# Рабочая директория внутри контейнера
WORKDIR /app

# Системные зависимости для GDAL и shapely.
# Ставим ДО pip install, чтобы кэш слоев работал корректно:
# если requirements.txt не меняется, apt не переустанавливается.
RUN apt-get update && apt-get install -y --no-install-recommends \
        libgdal-dev \
        gdal-bin \
        libgeos-dev \
        libproj-dev \
        build-essential \
    && rm -rf /var/lib/apt/lists/*

# Копируем только requirements, чтобы использовать Docker-кэш
COPY requirements.txt .

RUN pip install --no-cache-dir --upgrade pip && \
    pip install --no-cache-dir -r requirements.txt

# Копируем сам код
COPY app/ ./app/

# Создаем папку для экспортов (позже примонтируем volume)
RUN mkdir -p /app/exports

# Открываем порт (документация)
EXPOSE 8000

# Запускаем uvicorn
# --host 0.0.0.0 обязателен: иначе сервер слушает только localhost ВНУТРИ контейнера,
# и снаружи его не будет видно
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]