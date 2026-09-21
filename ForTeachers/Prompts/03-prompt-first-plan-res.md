# Отчёт: первая сессия планирования и реализации (02-prompt-first-plan → результат)

Дата: 2026-09-21
Файл: результат работы по промпту [02-prompt-first-plan.md](02-prompt-first-plan.md)

---

## 1. Исходная задача (кратко)

MCP-сервер, превращающий локальную папку документов в поисковую базу знаний:

- индексация: чанкинг → эмбеддинги multilingual-e5-small (ONNX, 384) → SQLite `vectorDb` + FTS5;
- повторный запуск: неизменённые файлы пропускаются по хешу;
- поиск: гибридный (векторный + BM25) → RRF → топ-10;
- Grade Chunks через локальную LLM (Ollama) с расширением запроса до 2 раз;
- 4 MCP-инструмента: `index_folder`, `ask_question`, `find_relevant_docs`, `index_status`;
- финальный ответ формирует агент-хост; MCP возвращает JSON с чанками и метаданными.

Полный текст ТЗ — в [02-prompt-first-plan.md](02-prompt-first-plan.md).

## 2. Скилы (.agents/skills)

Изучены и применены:

- [`mcp-csharp/SKILL.md`](../../.agents/skills/mcp-csharp/SKILL.md) — официальный SDK
  ModelContextProtocol, `[McpServerTool]`, содержательные description, DI, транспорт
  отдельно от бизнес-логики, HTTP Streamable/Stateless;
- [`aspnet/dotnet-webapi/SKILL.md`](../../.agents/skills/aspnet/dotnet-webapi/SKILL.md) —
  соглашения DTO (sealed records), конфигурация, DI.

## 3. Уточняющие вопросы и решения

### Вопрос 1: MD5 или SHA-256, путь БД, Ollama, порог релевантности, генерация ответа
**Ответ пользователя:**
- Хеш — **SHA-256** (в «Сущностях» было MD5 — противоречие устранено).
- БД — путь в `appsettings.json`, по умолчанию `Resources/vectorDb.db`.
- Ollama — настройки в конфиге: `baseURL = http://localhost:11434/v1`, модель `qwen2.5:3b`.
- Порог «достаточно релевантных» — **≥ 3 из топ-10** (настраивается).
- Вопрос про Generate Answer — пользователь сначала не понял, уточнён отдельно.

### Вопрос 2: что такое Generate Answer и одна ли модель для всех задач
**Ответ пользователя (ключевое решение):**
- Ollama используется **только** для Grade Chunks и **расширения запроса** (когда
  релевантных недостаточно — граф расширяет запрос и повторяет поиск).
- `find_relevant_docs` — **без LLM**.
- `ask_question` — после подготовки релевантных чанков они передаются **первому агенту,
  обратившемуся к MCP**; Ollama конечный ответ НЕ генерирует.

### Вопрос 3: Docker
MCP-сервер будет работать в Docker, Ollama — локально на хосте.

**Решение:** вся конфигурация через переменные окружения (`Section__Key`);
внутри контейнера `localhost` — это контейнер, поэтому `Ollama__BaseUrl`
= `http://host.docker.internal:11434/v1` (Linux: `extra_hosts: ["host.docker.internal:host-gateway"]`).
Тома: `./data:/data` (БД), `./docs:/docs` (документы). Требуется
`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` для Windows-1251 на Linux.

### Вопрос 4: логирование
**Ответ пользователя:** писать в стандартный поток вывода **и** в файл; уровень — от Information.

### Вопрос 5: технические подтверждения
- `token_type_ids` обязателен — передаётся нулями.
- `IncludeNativeLibrariesForSelfExtract` — включить (single-file ONNX).
- FTS5-запрос — экранировать спецсимволы.
- Векторный поиск — brute force (ANN не нужен).
- BM25 — штатно через SQLite FTS5 с триггерами.

## 4. План работ

Создан [plans/01-rag-mcp-plan.md](../../plans/01-rag-mcp-plan.md): 5 этапов,
диаграммы индексации/поиска/Corrective RAG, структура проекта, контракты инструментов,
раздел Docker (env, compose, Dockerfile), риски.

Сознательные отклонения от ТЗ (согласованы):
- триггеры FTS5 на INSERT/DELETE по `Chunks` (а не на удалении эмбеддинга);
- «перестройка BM25 в памяти» не нужна — индекс живёт в SQLite;
- имена входов/выходов ONNX определяются автоматически (`InputMetadata`);
- длины последовательностей — динамические, паддинг до длины батча.

## 5. Реализация (этапы)

### Этап 0 — подготовка
NuGet: MediatR 14.2.0, Microsoft.Data.Sqlite 10.0.12, Microsoft.ML.OnnxRuntime 1.30.0,
Tokenizers.HuggingFace 3.23.1, Serilog.AspNetCore 10.0.0, Serilog.Sinks.File 7.0.0.
`appsettings.json` (Database, Embedding, Chunking, Indexing, Retrieval, Ollama, Serilog),
`IncludeNativeLibrariesForSelfExtract=true`.

### Этап 1 — индексация
`Domain/` (Document, Chunk, Embedding); `SqliteConnectionFactory` (WAL, FK);
`SchemaInitializer` (DDL + триггеры `chunks_ai`/`chunks_ad` + уникальный индекс
`UQ_Chunks_DocumentId_ChunkIndex`); репозитории (соединение+транзакция вызывающего);
`TextFileReader` (BOM → UTF-8 → Windows-1251); `FileScanner` (glob + SHA-256);
`TextChunker` (regex по предложениям/переносам, один проход токенизатора, 480/20);
`TokenizerProvider` (Tokenizers.HuggingFace: `Encode(text, addSpecialTokens, includeTypeIds, includeAttentionMask)`);
`E5SmallEmbedder` (ONNX: input_ids, attention_mask, token_type_ids=0 → last_hidden_state →
mean pooling → L2; батчинг; lowercase + `passage:`/`query:`);
`IndexerService` + `IndexFolderRequest` (MediatR).

### Этап 2 — поиск
`VectorSearchService` (brute force dot product), `FtsRepository` (MATCH + `bm25()` +
экранирование токенов; исправлено: для external-content FTS5 используется `rowid`),
`RrfFusion` (k=60), `HybridSearchService`, `FindRelevantDocsQuery`.

### Этап 3 — Corrective RAG
`OllamaClient` (`/v1/chat/completions`), `ChunkGrader` (строгий JSON `{"relevant": bool}`,
нестрогий разбор), `QueryExpander`, `CorrectiveRagPipeline` (цикл ≤ 2 расширений,
порог `MinRelevant`), `AskQuestionQuery`.

### Этап 4 — MCP-инструменты и DI
`Tools/RagTools.cs`: 4 инструмента с `[McpServerTool]` и детальными description;
`Program.cs`: Serilog (stdout + файл), регистрация опций/сервисов/MediatR,
инициализация схемы БД при старте; `.http`-файл для тестов.
`Tools/RandomNumberTools.cs` оставлен как пример по просьбе пользователя
(не зарегистрирован).

## 6. Проверка и тесты (запуск http://localhost:6543)

| Инструмент | Результат |
|---|---|
| `index_status` | JSON-RPC: 0 документов до индексации, путь БД корректен |
| `index_folder` | 35 файлов → 276 чанков за ~13.3 c, ошибок 0 |
| `index_folder` (повторно) | 35 unchanged за ~6 мс (SHA-256 работает) |
| `find_relevant_docs` | JSON с метаданными (файл, chunkId, vectorScore, bm25Score, rrfScore); топ по запросу «образование Миронова» — dossier_001 |
| `ask_question` | ОТЛОЖЕН: Ollama на машине не запущена; проверена устойчивость (чанки → нерелевантные, пайплайн не падает) |

Замечание при тестировании: найден и исправлен баг FTS5 — `no such column: Id`
(для external-content таблицы нужен `rowid`).

## 7. Документация

- Память проекта: [`.agents/memory-bank`](../../.agents/memory-bank)
  (01-project-brief, 02-architecture, 03-progress).
- [`agent.md`](../../agent.md) — ссылки на память и скилы.
- [`README.md`](../../README.md) — полное описание, конфигурация, Docker.
- Логи запуска: `rag-server-out.txt` (сервер ещё работал на момент написания).

## 8. Что осталось

1. Тест `ask_question` с реальной Ollama (`ollama pull qwen2.5:3b`).
2. Этап 5 — Docker: Dockerfile, docker-compose.yml, документация (в плане готовы шаблоны).
3. Обновить `ForTeachers/report.md`.
4. Почистить временные файлы (`rag-server-out.txt`, `rag-server-err.txt`, `test-calls/`).