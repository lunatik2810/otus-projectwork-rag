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

## Известные нюансы

- При недоступной Ollama каждый вызов грейдинга занимает ~4 c (отказ соединения) —
  пайплайн замедляется, но не падает.
- MediatR логирует предупреждение о лицензии Lucky Penny (dev-режим разрешён).
- Модель принимает динамическую длину последовательности; паддинг выполняется до длины батча.