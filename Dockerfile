# ============================================================
# Multi-stage сборка MCP-сервера Corrective RAG (.NET 10, linux-x64)
# ============================================================

# --- Этап 1: сборка и публикация ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем весь контекст (Resources с ONNX-моделью входят через .dockerignore).
COPY . .

# Framework-dependent публикация под linux-x64:
# рантайм берётся из aspnet-образа на этапе runtime (быстрее и меньше по размеру,
# чем self-contained). RID задаём явно, чтобы в выходную папку попали
# native-библиотеки ONNX Runtime именно для linux-x64.
RUN dotnet publish otus-projectwork-rag.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o /app/publish

# --- Этап 2: рантайм-образ ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# URL ONNX-модели multilingual-e5-small (235 МБ).
# Модель НЕ хранится в git (превышает лимит GitHub 100 MiB на файл) —
# она публикуется как asset GitHub Release и скачивается при сборке образа.
# Заменить можно через --build-arg MODEL_URL=...
ARG MODEL_URL=https://github.com/lunatik2810/otus-projectwork-rag/releases/latest/download/model_O4.onnx

# curl нужен для healthcheck и скачивания модели (в aspnet-образе его нет по умолчанию).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Скачиваем модель, если она не попала в образ из контекста сборки
# (при локальной разработке, когда файл уже лежит в Resources/, шаг пропускается).
RUN if [ ! -f /app/Resources/multilingual-e5-small/model_O4.onnx ]; then \
      mkdir -p /app/Resources/multilingual-e5-small \
      && echo "Модель не найдена в контексте, скачиваю: ${MODEL_URL}" \
      && curl -fSL -o /app/Resources/multilingual-e5-small/model_O4.onnx "${MODEL_URL}"; \
    fi

# Токенизатор и конфиг модели копируются из контекста (они в git и в /app/Resources).
# Пути к БД, логам и Ollama задаются переменными окружения (см. docker-compose.yml).
EXPOSE 8080

ENTRYPOINT ["dotnet", "otus-projectwork-rag.dll"]