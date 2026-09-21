# Агент: инструкции по работе с репозиторием

Этот файл — точка входа для агентов (Roo, Copilot и др.), работающих в репозитории
`otus-projectwork-rag`. Перед началом работы прочитайте:

## Память проекта (.agents/memory-bank)

- [01-project-brief.md](.agents/memory-bank/01-project-brief.md) — назначение проекта,
  ключевые технологии, бизнес-правила и принятые решения.
- [02-architecture.md](.agents/memory-bank/02-architecture.md) — архитектура, схема БД,
  триггеры FTS5, особенности модели multilingual-e5-small, состав сервисов.
- [03-progress.md](.agents/memory-bank/03-progress.md) — статус работ, что проверено,
  что осталось, известные нюансы.

## Скилы (.agents/skills)

Используйте при выполнении соответствующих задач:

- [mcp-csharp/SKILL.md](.agents/skills/mcp-csharp/SKILL.md) — разработка MCP-серверов на C#
  (обязателен для любых правок инструментов/транспорта MCP).
- [aspnet/dotnet-webapi/SKILL.md](.agents/skills/aspnet/dotnet-webapi/SKILL.md) —
  ASP.NET Core Web API: endpoints, DTO, OpenAPI.
- [aspnet/minimal-api-file-upload/SKILL.md](.agents/skills/aspnet/minimal-api-file-upload/SKILL.md) —
  загрузка файлов в minimal API.
- [aspnet/configuring-opentelemetry-dotnet/SKILL.md](.agents/skills/aspnet/configuring-opentelemetry-dotnet/SKILL.md) —
  настройка OpenTelemetry в .NET.
- [aspnet/convert-blazor-server-to-webapp/SKILL.md](.agents/skills/aspnet/convert-blazor-server-to-webapp/SKILL.md) —
  миграция Blazor Server → Web App.
- [dotnet/setup-local-sdk/SKILL.md](.agents/skills/dotnet/setup-local-sdk/SKILL.md) —
  установка локального .NET SDK.

## Правила работы

1. Общаться с пользователем на русском языке.
2. Следовать плану: [plans/01-rag-mcp-plan.md](plans/01-rag-mcp-plan.md).
3. Комментарии в коде — на русском.
4. Логирование — Serilog (stdout + файл), минимальный уровень Information.
5. Изменённые/добавленные этапы отражать в `.agents/memory-bank/03-progress.md`.
6. Не удалять `Tools/RandomNumberTools.cs` — это пример; он не зарегистрирован в Program.cs.