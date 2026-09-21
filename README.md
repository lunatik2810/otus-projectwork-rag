# MCP-сервер Corrective RAG на базе SQLite + multilingual-e5-small + Ollama

MCP-сервер, который превращает локальную папку с документами в поисковую базу знаний.
Разработчик подключает сервер к IDE (Copilot, Claude и др.), индексирует документы и задаёт
вопросы — AI в IDE получает ответы, основанные на содержимом файлов.

Внутри сервера — **Corrective RAG-пайплайн** с локальной LLM через **Ollama**
(без платных API) и векторными эмбеддингами **multilingual-e5-small** (ONNX, 384 измерения).

## Возможности

- **Индексация папки**: файлы делятся на чанки (470 токенов, перекрытие 20), каждый чанк
  превращается в вектор (mean pooling + L2), всё сохраняется в SQLite (`vectorDb.db`)
  вместе с полнотекстовым индексом FTS5 (BM25). Неизменённые файлы пропускаются по SHA-256,
  изменённые — переиндексируются полностью.
- **Гибридный поиск**: BM25 (точные слова) + векторная близость (смысл) → объединение
  через Reciprocal Rank Fusion (RRF, k=60) → топ-10.
- **Grade Chunks (Corrective RAG)**: локальная LLM оценивает релевантность найденных
  фрагментов (yes/no); если релевантных мало — запрос расширяется и поиск повторяется
  (до 2 раз). Финальный ответ формулирует **агент-хост** в IDE: сервер возвращает
  JSON с фрагментами и метаданными (файл, id чанка, оценки близости).
- **Работает на Windows и Linux** (в том числе в Docker-контейнере).

## MCP-инструменты

| Инструмент | Вход | Что делает |
|---|---|---|
| `index_folder` | `folderPath`, `globPattern` (`*.txt`) | Сканирует папку, чанкирует, строит эмбеддинги, сохраняет в БД; неизменённые файлы пропускает |
| `ask_question` | `question` | Полный Corrective RAG-цикл: гибридный поиск → грейдинг Ollama → при нехватке релевантных — расширение запроса (до 2 раз) → отобранные чанки агенту |
| `find_relevant_docs` | `query`, `topK` (10) | Ранжированные чанки без генерации ответа (BM25 + вектор → RRF), без LLM |
| `index_status` | — | Статистика: документы, чанки, векторы, время последней индексации |

Ответы возвращаются в JSON: для каждого чанка — текст, имя файла, путь, `chunkId`,
`documentId`, оценки `vectorScore`, `bm25Score`, `rrfScore`.

## Структура проекта

```
Contracts/            DTO результатов (IndexFolderResult, SearchResult, AskQuestionResult, ...)
Domain/               Сущности: Document, Chunk, Embedding
Configuration/        Классы опций (секции appsettings.json)
Infrastructure/
  Data/               SqliteConnectionFactory, SchemaInitializer, репозитории
  Text/               TextFileReader (кодировки), TextChunker, FileScanner
  Embeddings/         TokenizerProvider, E5SmallEmbedder (ONNX)
  Search/             VectorSearchService, Bm25 (FtsRepository), RrfFusion, HybridSearchService
  Ollama/             OllamaClient (OpenAI-совместимый /v1/chat/completions)
  Rag/                ChunkGrader, QueryExpander, CorrectiveRagPipeline
Features/             MediatR: Request + Handler рядом (IndexFolder, FindRelevantDocs, AskQuestion, IndexStatus)
Tools/                RagTools — 4 MCP-инструмента ([McpServerTool])
plans/                План работ
```

## Конфигурация

Все настройки — в [appsettings.json](appsettings.json) и переопределяются переменными
окружения `Section__Key`:

| Переменная | Значение по умолчанию | Назначение |
|---|---|---|
| `Database__Path` | `Resources/vectorDb.db` | Путь к файлу SQLite-базы |
| `Ollama__BaseUrl` | `http://localhost:11434/v1` | OpenAI-совместимый API Ollama |
| `Ollama__Model` | `qwen2.5:3b` | Модель грейдинга/расширения запроса |
| `Embedding__ModelDirectory` | `Resources/multilingual-e5-small` | Каталог ONNX-модели и токенизатора |
| `Chunking__MaxTokens` | `470` | Максимум токенов в чанке |
| `Chunking__OverlapTokens` | `20` | Перекрытие между чанками |
| `Retrieval__MinRelevant` | `3` | Порог релевантных чанков для остановки цикла |
| `Retrieval__MaxExpansions` | `2` | Максимум расширений запроса |
| `Serilog__MinimumLevel__Default` | `Information` | Уровень логирования |

## Запуск (локально)

Требуется .NET 10 SDK и запущенная [Ollama](https://ollama.com/) с моделью
(`ollama pull qwen2.5:3b`).

```bash
dotnet run --launch-profile http
```

Сервер слушает `http://localhost:6543`. Примеры вызовов инструментов — в
[otus-projectwork-rag.http](otus-projectwork-rag.http).

Подключение из VS Code:

```json
{
  "servers": {
    "otus-projectwork-rag": {
      "type": "http",
      "url": "http://localhost:6543"
    }
  }
}
```

## Docker

Сервер рассчитан на запуск в контейнере: конфигурация передаётся переменными окружения,
папка документов и БД монтируются томами. Ключевой момент — **Ollama на хосте**, поэтому
внутри контейнера используйте `http://host.docker.internal:11434/v1` (на Linux добавьте
`extra_hosts: ["host.docker.internal:host-gateway"]`).

Пример `docker-compose.yml` (полный вариант — в плане
[plans/01-rag-mcp-plan.md](plans/01-rag-mcp-plan.md#6-развёртывание-в-docker)):

```yaml
services:
  rag-mcp:
    build: .
    ports:
      - "6543:8080"
    environment:
      ASPNETCORE_URLS: "http://+:8080"
      Ollama__BaseUrl: "http://host.docker.internal:11434/v1"
      Ollama__Model: "qwen2.5:3b"
      Database__Path: "/data/vectorDb.db"
      Indexing__DefaultGlob: "*.txt"
    volumes:
      - ./data:/data
      - ./docs:/docs
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

## Документация и память

- **План работ**: [plans/01-rag-mcp-plan.md](plans/01-rag-mcp-plan.md)
- **Память проекта (для агентов)**: [.agents/memory-bank](.agents/memory-bank)
  - [01-project-brief.md](.agents/memory-bank/01-project-brief.md) — сводка проекта
  - [02-architecture.md](.agents/memory-bank/02-architecture.md) — архитектура и БД
  - [03-progress.md](.agents/memory-bank/03-progress.md) — статус работ
- **Скилы**: [.agents/skills](.agents/skills) — mcp-csharp, aspnet/dotnet-webapi и др.
- **Материалы для преподавателя**: [ForTeachers/report.md](ForTeachers/report.md),
  [ForTeachers/Prompts](ForTeachers/Prompts)

## Официальные материалы по MCP

- [C# MCP SDK](https://csharp.sdk.modelcontextprotocol.io/)
- [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)
- [Документация MCP](https://modelcontextprotocol.io/)
- [Использование MCP в VS Code](https://code.visualstudio.com/docs/copilot/chat/mcp-servers)
