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

# curl нужен только для healthcheck (в aspnet-образе его нет по умолчанию).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Модель и токенизатор уже внутри /app/Resources (копируются через csproj).
# Пути к БД, логам и Ollama задаются переменными окружения (см. docker-compose.yml).
EXPOSE 8080

ENTRYPOINT ["dotnet", "otus-projectwork-rag.dll"]