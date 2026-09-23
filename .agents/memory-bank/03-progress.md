# Статус работ

## Завершено

- [x] **Этап 0** — пакеты NuGet, `appsettings.json`, env-конфигурация, single-file + native ONNX.
- [x] **Этап 1** — домен, фабрика SQLite (WAL/FK), схема + триггеры FTS5, репозитории,
      чтение файлов (BOM/кодировки), чанкер 480/20, эмбеддер (ONNX, батчинг),
      IndexerService + MediatR (пропуск по SHA-256, транзакция на файл).
- [x] **Этап 2** — векторный поиск (brute force), BM25 (FTS5 MATCH + экранирование), RRF,
      HybridSearchService + FindRelevantDocsQuery.
- [x] **Этап 3** — OllamaClient (OpenAI-совместимый), ChunkGrader, QueryExpander,
      CorrectiveRagPipeline (до 2 расширений, порог MinRelevant=3), AskQuestionQuery.
- [x] **Этап 4** — MCP-инструменты `index_folder`, `find_relevant_docs`, `ask_question`,
      `index_status` ([McpServerTool], description), Serilog (stdout + файл), DI.
- [x] **Проверка сборки** — `dotnet build` проходит без ошибок/предупреждений.
- [x] **Этап 5 (Docker)** — созданы [`Dockerfile`](../../Dockerfile) (multi-stage
      sdk:10.0 → aspnet:10.0, framework-dependent linux-x64, curl для healthcheck),
      [`docker-compose.yml`](../../docker-compose.yml) (env `Ollama__BaseUrl=host.docker.internal`
      + `Ollama__Model` через .env-переменные с дефолтами, тома `./data`, `./docs`, `./logs`,
      `extra_hosts: host.docker.internal:host-gateway`, healthcheck на `/health`, `restart`),
      [`.env.example`](../../.env.example) (`OLLAMA_MODEL`, `OLLAMA_BASE_URL`, `RAG_MCP_PORT`),
      [`.dockerignore`](../../.dockerignore) (исключены bin/obj/.git/Tests/data/docs/logs,
      `Resources` с ONNX-моделью остаются в контексте); в `Program.cs` добавлен endpoint
      `/health`; README — раздел Docker переписан под запуск преподавателем
      (`git clone && docker compose up`).

## Документация

- [x] Создан [`ARCHITECTURE.md`](../../ARCHITECTURE.md) — полное описание архитектуры
      по фактическому состоянию кода: стек, схема потоков (индексация / гибридный поиск /
      Corrective RAG), схема БД и триггеры FTS5, особенности multilingual-e5-small,
      MCP-инструменты, конфигурация, Docker, тесты, принятые решения и ограничения.
- [x] Создан [`LICENSE`](../../LICENSE) — лицензия проекта (MIT) + Third-Party Notices:
      лицензии всех NuGet-зависимостей проверены по .nuspec/LICENSE в локальном кэше
      (MediatR/MCP/Serilog/Tokenizers.HuggingFace — Apache-2.0; Microsoft.Data.Sqlite/
      OnnxRuntime — MIT; Google.Protobuf — BSD-3-Clause; SQLitePCLRaw — Apache-2.0)
      плюс разделы о тестовых пакетах, ONNX-модели multilingual-e5-small (MIT по карточке
      HF) и демо-данных.

## Проверено вручную (запуск сервера на http://localhost:6543)

- [x] `index_status` — корректный JSON-RPC-ответ (0 документов до индексации).
- [x] `index_folder` — 35 файлов, 276 чанков, ~13.3 c, без ошибок.
- [x] Повторный `index_folder` — 35 unchanged (SHA-256 работает), ~6 мс.
- [x] `find_relevant_docs` — JSON с метаданными (файл, chunkId, vectorScore, bm25Score, rrfScore);
      исправлен FTS5-запрос (`rowid` для external-content таблицы).
- [ ] `ask_question` с реальной Ollama — **не протестировано**: Ollama на машине не запущена;
      устойчивость к недоступной Ollama проверена (чанки помечаются нерелевантными, пайплайн не падает).

## Осталось

- [x] Тест `ask_question` при запущенной Ollama (`ollama pull qwen2.5:3b`) — автоматизирован
      тестом `AskQuestion_WithRealOllama_PipelineRunsAndReturnsChunks` (запускает пользователь;
      при недоступной Ollama тест не падает — resilience-путь).
- [ ] Обновить `ForTeachers/report.md`.
- [ ] Почистить временные файлы запуска (`rag-server-out.txt`, `rag-server-err.txt`, `test-calls/*`).

## Тесты (новый этап)

- [x] **Первый тест индексации** — имитация вызова MCP-инструмента `index_folder`
      ([`RagTools.IndexFolder`](../../Tools/RagTools.cs)) БЕЗ запуска MCP-сервера:
  - [`Tests/testProjectMCP-RAG/RagTestHost.cs`](../../Tests/testProjectMCP-RAG/RagTestHost.cs) —
    DI-хост (зеркало регистраций из `Program.cs` для цепочки индексации),
    временные файлы — в `Tests/testProjectMCP-RAG/.rag-test-tmp` (не %TEMP%),
    автоподчистка в `Dispose` (`SqliteConnection.ClearAllPools()` + ретраи удаления);
  - [`Tests/testProjectMCP-RAG/IndexFolderToolTests.cs`](../../Tests/testProjectMCP-RAG/IndexFolderToolTests.cs) —
    вариант A: прямой вызов инструмента `RagTools.IndexFolder` — проверка `Added=3`, затем `Unchanged=3`;
    вариант B: повтор кода изнутри метода — `mediator.Send(new IndexFolderRequest(...))`,
    имя намеренно другое (`MediatorSend_IndexFolderRequest_...`);
  - проверено: `dotnet build` — 0 предупреждений; прямой запуск тестового exe — 2/2 успешно (~2 с);
    обозреватель тестов VS — работает;
  - [ ] **нюанс:** CLI `dotnet test` на SDK 10.0.401 + MTP 2.4.0 (xunit.v3) завершается
        кодом 5 «запущено ноль тестов» — несовместимость MTP-клиента SDK и платформы;
        конфигурация корректная (`global.json` runner=MTP, `UseMicrosoftTestingPlatformRunner`,
        прямая ссылка `Microsoft.Testing.Platform`, без VSTest-пакетов);
        рабочий запуск: `dotnet build Tests\testProjectMCP-RAG\testProjectMCP-RAG.csproj`
        + `dotnet Tests\testProjectMCP-RAG\bin\Debug\net10.0\testProjectMCP-RAG.dll`.

- [x] **Тесты `ask_question`** — имитация вызова MCP-инструмента `ask_question`
      ([`RagTools.AskQuestion`](../../Tools/RagTools.cs)) БЕЗ запуска MCP-сервера,
      по аналогии с FindRelevantDocsToolTests:
  - [`Tests/testProjectMCP-RAG/AskQuestionToolTests.cs`](../../Tests/testProjectMCP-RAG/AskQuestionToolTests.cs) —
    полная цепочка инструмент → MediatR → CorrectiveRagPipeline (гибридный поиск →
    грейдинг → расширение запроса) → SQLite, 6 тестов:
    - все чанки релевантны → Attempts=1, расширений нет;
    - Ollama недоступна (заглушка бросает) → пайплайн не падает: Attempts=3, все чанки нерелевантны;
    - мало релевантных → расширение запроса → повторный поиск успешен (Attempts=2);
    - пустая БД → пустой результат без исключений;
    - метаданные и оценки чанков заполнены;
    - реальная Ollama (`new RagTestHost()` без заглушки) → пайплайн отрабатывает,
      при недоступной Ollama не падает;
  - [`Tests/testProjectMCP-RAG/RagTestHost.cs`](../../Tests/testProjectMCP-RAG/RagTestHost.cs) —
    расширен до полного зеркала Program.cs: добавлены RetrievalOptions, OllamaOptions и
    RAG-цепочка (OllamaClient, ChunkGrader, QueryExpander, CorrectiveRagPipeline);
    опциональный параметр `ollamaOverride` позволяет подставить заглушку IOllamaClient
    (детерминированные тесты без сети); существующие тесты не затронуты;
  - проверено: `dotnet build Tests\testProjectMCP-RAG\testProjectMCP-RAG.csproj` —
    0 ошибок, 0 предупреждений; запуск тестов — пользователем (способ запуска см. выше).

### Инфраструктурные правки под тесты

- Основной csproj: `SelfContained`/`PublishSingleFile` вынесены в параметры
  `dotnet publish` (иначе NETSDK1151 — нельзя ссылаться на self-contained exe);
  добавлен `ProduceReferenceAssembly=true`; каталог `Tests\**` исключён из компиляции
  (default-glob захватывал исходники и сгенерированные файлы тестов → дубли атрибутов).
- Тестовый проект: xunit.v3 + Microsoft.Testing.Platform (чистый MTP, без VSTest-пакетов),
  `OutputType=Exe`, `UseAppHost=true`; ссылка на основной проект.

## Известные нюансы

- При недоступной Ollama каждый вызов грейдинга занимает ~4 c (отказ соединения) —
  пайплайн замедляется, но не падает.
- MediatR логирует предупреждение о лицензии Lucky Penny (dev-режим разрешён).
- Модель принимает динамическую длину последовательности; паддинг выполняется до длины батча.