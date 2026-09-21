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

## Проверено вручную (запуск сервера на http://localhost:6543)

- [x] `index_status` — корректный JSON-RPC-ответ (0 документов до индексации).
- [x] `index_folder` — 35 файлов, 276 чанков, ~13.3 c, без ошибок.
- [x] Повторный `index_folder` — 35 unchanged (SHA-256 работает), ~6 мс.
- [x] `find_relevant_docs` — JSON с метаданными (файл, chunkId, vectorScore, bm25Score, rrfScore);
      исправлен FTS5-запрос (`rowid` для external-content таблицы).
- [ ] `ask_question` с реальной Ollama — **не протестировано**: Ollama на машине не запущена;
      устойчивость к недоступной Ollama проверена (чанки помечаются нерелевантными, пайплайн не падает).

## Осталось

- [ ] Тест `ask_question` при запущенной Ollama (`ollama pull qwen2.5:3b`).
- [ ] **Этап 5 (Docker)**: Dockerfile (multi-stage linux-x64 self-contained),
      docker-compose.yml (env, тома `./data` и `./docs`, `extra_hosts: host.docker.internal`),
      документация.
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