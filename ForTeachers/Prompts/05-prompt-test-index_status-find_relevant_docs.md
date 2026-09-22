# Сессия: логи в тестах, тест index_status, тесты find_relevant_docs, документы из файлов

Дата: 2026-09-22
Файл: протокол переписки (продолжение [04-prompt-test-indexing.md](04-prompt-test-indexing.md))

---

## 1. Запросы пользователя (исходные промпты)

> 1. «Запускаю тест `IndexFolder_DirectCallWithoutMcpServer_IndexesFolderAndReturnsStats`.
>    А куда пишутся логи, например когда из теста попадаю в файл `Tools/RagTools.cs`
>    на строку 43, где `logger.LogInformation(...)`? Не могу найти. Или при тестах
>    они не пишутся?»
>
> 2. «Сделай Вариант B» (подключить Serilog в тестовом хосте).
>
> 3. «Смотрю этот же тест, не могу понять как данные попадают в таблицу
>    `ChunksFts_data` и что за дополнительные таблицы `ChunksFts_{prefix}`».
>
> 4. «Теперь давай напишем тест на Tool `IndexStatus`. Это в файле `Tools/RagTools.cs`
>    функция `public async Task<IndexStatusResult> IndexStatus(...)`. Сделай по аналогии
>    с тестом `IndexFolder_DirectCallWithoutMcpServer_IndexesFolderAndReturnsStats`.
>    Сделай один тест. Сначала вызови, чтобы он пустой был, потом прогони один файл,
>    который первый (`doc_001.txt`) из `Tests/testProjectMCP-RAG/RagTestHost.cs`».
>
> 5. «Теперь давай по аналогии напишем новый тест для Tool `find_relevant_docs`.
>    Нужно вызвать сначала `var first = await tools.IndexFolder(host.DocumentsDir, "*.txt", ...)`,
>    а потом уже проверять поиск. Предложи несколько тестов».
>
> 6. «Я помню в поиске была логика, что нужно найти top 50 в векторном поиске и
>    топ 50 в семантическом, а потом по RRF выбрать 10. Нужен еще тест, который найдет
>    хотя бы в одном больше 12 чанков, чтобы потом обрезать. Текст расширил в файлах.
>    Тебе еще нужно изменить чтение данных в `RagTestHost.cs`. Я создал файлы в папке
>    `otus-projectwork-rag\Tests\testProjectMCP-RAG\Resources\DataBaseRAG`, оттуда должен
>    брать документы, а не `File.WriteAllText(...)`».
>
> 7. «У меня Ollama не запущена, сама позже проверю».
>
> 8. «Отлично, сохрани всю нашу переписку в `05-prompt-test-index_status-find_relevant_docs.md`,
>    это в папку `otus-projectwork-rag\ForTeachers\Prompts`».

## 2. Разбор и решения

### 2.1 Куда пишутся логи при тестах

Изучены [`Program.cs`](../../Program.cs), [`RagTestHost.cs`](../../Tests/testProjectMCP-RAG/RagTestHost.cs),
[`RagTools.cs`](../../Tools/RagTools.cs). Ответ: **при тестах `LogInformation` не пишется никуда** —
две независимые причины:

1. **Serilog настроен только в [`Program.cs`](../../Program.cs:33)** через `builder.Host.UseSerilog(...)`
   внутри хоста `WebApplication` реального MCP-сервера (консоль + файл `logs/rag-*.log`).
   Тестовый хост собирает DI вручную через `ServiceCollection` и Serilog не подключает.
2. **Уровень фильтрации — `Warning`**: `services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning))`
   в [`RagTestHost.cs`](../../Tests/testProjectMCP-RAG/RagTestHost.cs). Уровень Information ниже Warning,
   поэтому `ILogger<RagTools>` отбрасывает сообщение до вызова провайдеров.

Даже при понижении уровня сообщения ушли бы в `ConsoleLoggerProvider` (stdout тест-процесса),
который при `dotnet test` с xunit.v3 + MTP по умолчанию не отображается.

Варианты решения: A — понизить минимальный уровень; B — подключить Serilog в тестовый хост
(пакеты транзитивно доступны из основного проекта); C — `ITestOutputHelper`.

### 2.2 Вариант B — Serilog в тестовом хосте

Выбран вариант B:

- `using Serilog;` + статические поля `SerilogSync`/`_serilogConfigured`;
- метод `EnsureSerilogConfigured()` — глобальный `Log.Logger` инициализируется **один раз на
  процесс под lock** (xunit.v3 запускает классы параллельно, `Log.Logger` — статический синглтон);
  силки: консоль + файл `test-rag-.log` с дневной ротацией (`retainedFileCountLimit: 14`);
- регистрация: `AddLogging(builder => { builder.SetMinimumLevel(LogLevel.Information); builder.AddSerilog(dispose: false); })`
  — `dispose: false`, чтобы `Dispose` одного хоста не закрывал общий логгер других тестов.

Первоначально файл писался в корень `.rag-test-tmp`. **Замечание пользователя:**
«Логи должны писаться в папку `\otus-projectwork-rag\Tests\testProjectMCP-RAG\logs`, исправь» →
путь заменён на `Path.Combine(ResolveTestProjectDirectory(), "logs", "test-rag-.log")`.
Папка `logs` покрыта паттерном `[Ll]ogs/` в `.gitignore`.

Проверено: сборка без ошибок, тест проходит, в файле появились строки
`MCP-инструмент index_folder: папка ...`, завершение за 900 мс (3 файла, 3 чанка),
внутренние сообщения `IndexerService` и `WRN` MediatR о лицензии.

### 2.3 Как данные попадают в `ChunksFts_data` и что такое `ChunksFts_{prefix}`

Разобрано на примере теста (см. [`SchemaInitializer.cs`](../../Infrastructure/Data/SchemaInitializer.cs),
[`ChunkRepository.cs`](../../Infrastructure/Data/Repositories/ChunkRepository.cs),
[`FtsRepository.cs`](../../Infrastructure/Data/Repositories/FtsRepository.cs)):

- приложение **никогда не пишет в `ChunksFts` напрямую**: `IndexerService` делает обычный
  `INSERT INTO Chunks (...)`, а триггер `chunks_ai AFTER INSERT` (`SchemaInitializer.cs:102`)
  выполняет `INSERT INTO ChunksFts(rowid, Text) VALUES (new.Id, new.Text)` — это **команда
  индексации** для FTS5;
- `ChunksFts` объявлена с внешним контентом (`content='Chunks'`, `content_rowid='Id'`),
  поэтому сам текст в FTS не дублируется;
- при переиндексации триггер `chunks_ad` (`SchemaInitializer.cs:108`) шлёт команду `'delete'`;
- поиск идёт через `MATCH` + `bm25()` (`FtsRepository.cs:45`);
- таблицы `ChunksFts_data/_idx/_docsize/_content/_config` — **теневые таблицы FTS5**
  (инвертированный индекс, индекс по rowid, размеры документов для BM25, пустой контент,
  конфигурация) — деталь реализации SQLite, трогать их не нужно.

### 2.4 Тест index_status

Прямой вызов [`RagTools.IndexStatus`](../../Tools/RagTools.cs:137) без MCP-сервера, один тест,
три шага на одном хосте ([`IndexStatusToolTests.cs`](../../Tests/testProjectMCP-RAG/IndexStatusToolTests.cs)):

1. пустая БД → `DocumentCount=0`, `ChunkCount=0`, `EmbeddingCount=0`, `LastIndexedAt=null`
   (в [`IndexStatusQueryHandler`](../../Features/Status/IndexStatusQuery.cs:28) берётся
   `max(UpdatedAt)` по документам, документов нет), `DatabasePath` непустой;
2. индексация одного файла glob-паттерном (без разделителей пути — `FileScanner`
   сопоставляет с именем файла): `ScannedFiles=1`, `Added=1`, `Chunks>0`;
3. повторный статус: `DocumentCount=1`, `ChunkCount == indexed.Chunks`,
   `EmbeddingCount == ChunkCount`, `LastIndexedAt` не null, `DatabasePath` тот же.

### 2.5 Тесты find_relevant_docs (предложение → реализация)

Изучены [`FindRelevantDocsResult`](../../Contracts/FindRelevantDocsResult.cs),
[`SearchResult`](../../Contracts/SearchResults.cs), [`HybridSearchService`](../../Infrastructure/Search/HybridSearchService.cs),
конфигурация `Retrieval` (`Bm25TopK=50`, `VectorTopK=50`, `RrfK=60`, `FinalTopK=10` из
[`appsettings.json`](../../appsettings.json)). Предложено 6 тестов, пользователь одобрил и добавил
7-й — про каскад кандидатов («хотя бы один метод находит > 12 чанков, чтобы потом обрезать»).

**Важный нюанс:** тест «несуществующее слово → пусто» не делаем — векторная часть гибридного
поиска вернёт ближайшие чанки даже по случайному слову. В тесте «уникальное слово» нельзя
требовать «все результаты из одного файла» — только верхний результат и наличие нужного файла.

Реализованы 7 тестов в [`FindRelevantDocsToolTests.cs`](../../Tests/testProjectMCP-RAG/FindRelevantDocsToolTests.cs)
(общий префикс — свежий хост + `IndexFolder("*.txt")`; для тестов 1–4/6, тест 5 — хост без
индексации):

| № | Тест | Запрос | Проверки |
|---|---|---|---|
| 1 | `FindRelevantDocs_ExactWord_TopResultFromExpectedDossier` | «Миронов» (только dossier_001; «Миронова» в 003 — другой токен) | результаты не пусты; верхний по RRF — `dossier_001.txt` |
| 2 | `FindRelevantDocs_UniqueTerm_TopResultFromExpectedDossier` | «Казань» (только dossier_002) | верхний — `dossier_002.txt`; среди результатов есть чанк этого файла |
| 3 | `FindRelevantDocs_TopK_LimitsResultCountAndKeepsOrder` | «инженер по автоматизации» (001 и 002), `topK: 2` | `TopK==2`, `Results.Count==2`, сортировка по `RrfScore` по убыванию |
| 4 | `FindRelevantDocs_MetadataAndScores_AreFilled` | «финансовые» (во всех 5), `topK: 5` | у каждого: `ChunkId>0`, `TokenCount>0`, текст/имя непустые, `SourcePath` в `DocumentsDir`, `RrfScore>0` |
| 5 | `FindRelevantDocs_EmptyDatabase_ReturnsEmptyList` | «Миронов», хост без индексации | пустой список, без исключений |
| 6 | `FindRelevantDocs_BlankQuery_ReturnsEmptyList` | `"   "` | пустой список (ранний выход в `HybridSearchService.cs:55`) |
| 7 | `FindRelevantDocs_CandidatePipeline_HasMoreThanFinalTopKThenTruncates` | «финансовые» | прямые вызовы `IFtsRepository.SearchTop(topK:100)` и `IVectorSearchService.Search(topK:100)`: `max(BM25, векторных) > 12`; инструмент с `topK=10` возвращает `0 < Count <= 10` |

Для теста 7 в `RagTestHost` дополнительно зарегистрированы `IVectorSearchService`/`IHybridSearchService`
(их нет в исходном зеркале `Program.cs` для цепочки индексации).

## 3. Ход работы и замечания пользователя (диалог)

### 3.1 Рефакторинг RagTestHost — документы из файлов

По требованию пользователя блок `File.WriteAllText(...)` с инлайн-контентом (doc_001…005 + notes.md)
заменён на копирование всех файлов из `Resources/DataBaseRAG` в сессионную папку.
`DocCount = Directory.EnumerateFiles(DocumentsDir, "*.txt").Count()` — вычисляется динамически.
`notes.md` остаётся вне выборки `*.txt` (проверка glob-фильтра).

### 3.2 Найденная проблема: смешивание корпуса через ProjectReference

Первый прогон `FindRelevantDocs`: 9/10 успешно, упал тест «Казань» —
`Expected: "dossier_002.txt", Actual: "dossier_019.txt"`. По логам: индексировалось
**35 файлов / 283 чанка**, хотя в тестовой папке только 5.

**Причина:** источник читался из вывода сборки (`AppContext.BaseDirectory/Resources/DataBaseRAG`).
В `bin\...\Resources\DataBaseRAG` кроме 5 тестовых файлов попадают ещё **35 досье основного
проекта** ([`Resources/DataBaseRAG`](../../Resources/DataBaseRAG)) — контент ссылаемого exe-проекта
с `CopyToOutputDirectory` копируется в вывод ссылающегося проекта (стандартный механизм MSBuild
`GetCopyToOutputDirectoryItems`).

**Решение:** читать документы из исходного дерева тестового проекта —
`Path.Combine(ResolveTestProjectDirectory(), "Resources", "DataBaseRAG")`. Корпус теста — ровно
5 досье пользователя + `notes.md`.

### 3.3 Ошибка компиляции

`CS1503: Аргумент 2: не удается преобразовать из "System.Threading.CancellationToken" в "int"`
— в `FindRelevantDocs(query, TestContext.Current.CancellationToken)` токен отмены попал на место
`int topK`. Исправлено: `FindRelevantDocs(query, topK: 10, TestContext.Current.CancellationToken)`.

### 3.4 Пользователь: «Ollama не запущена, сама позже проверю»

Замечание принято: все добавленные тесты (`index_status`, `find_relevant_docs`) LLM **не используют**,
Ollama для них не нужна — это чистый BM25 + векторный поиск без генерации ответа.

## 4. Итоговое состояние (проверено)

- `dotnet build Tests/testProjectMCP-RAG/testProjectMCP-RAG.csproj` — 0 предупреждений, 0 ошибок;
- `dotnet test Tests/testProjectMCP-RAG/testProjectMCP-RAG.csproj` — **Total: 10, Failed: 0**
  (~30 с), включая регресс `IndexFolderToolTests` (2) и `IndexStatusToolTests` (1);
- сообщения `MCP-инструмент index_status...` / `MCP-инструмент find_relevant_docs...` пишутся
  в `Tests/testProjectMCP-RAG/logs/test-rag-*.log`.

### Созданные/изменённые файлы

- [`Tests/testProjectMCP-RAG/RagTestHost.cs`](../../Tests/testProjectMCP-RAG/RagTestHost.cs) —
  Serilog (консоль + `logs/test-rag-*.log`, `AddSerilog(dispose: false)`), документы копируются
  из исходного `Resources/DataBaseRAG` (не из вывода сборки!), `DocCount` динамический,
  добавлены `IVectorSearchService`/`IHybridSearchService`;
- [`Tests/testProjectMCP-RAG/IndexStatusToolTests.cs`](../../Tests/testProjectMCP-RAG/IndexStatusToolTests.cs) —
  тест `index_status` (пустая БД → индексация `dossier_001.txt` → статистика с 1 документом);
- [`Tests/testProjectMCP-RAG/FindRelevantDocsToolTests.cs`](../../Tests/testProjectMCP-RAG/FindRelevantDocsToolTests.cs) —
  7 тестов `find_relevant_docs`, включая проверку каскада кандидатов (> 12 до RRF, ≤ 10 после);
- [`Tests/testProjectMCP-RAG/Resources/DataBaseRAG/`](../../Tests/testProjectMCP-RAG/Resources/DataBaseRAG) —
  корпус теста (5 досье + notes.md, создан пользователем; csproj копирует в вывод сборки);
- [`Tests/testProjectMCP-RAG/testProjectMCP-RAG.csproj`](../../Tests/testProjectMCP-RAG/testProjectMCP-RAG.csproj) —
  `None Update` для `Resources\DataBaseRAG\*` с `CopyToOutputDirectory`;
- [`ForTeachers/Prompts/05-prompt-test-index_status-find_relevant_docs.md`](05-prompt-test-index_status-find_relevant_docs.md) —
  данный протокол.

## 5. Известные нюансы

1. **Корпус теста не должен читаться из вывода сборки**: из-за ProjectReference в
   `bin\...\Resources\DataBaseRAG` попадают 35 досье основного проекта (283 чанка) — корпус
   теста непредсказуемо расширяется, и проверки «уникального слова» ломаются.
   Источник — всегда исходное дерево тестового проекта.
2. **Гибридный поиск всегда «что-то найдёт»**: векторная часть возвращает ближайшие чанки
   даже по случайному/несуществующему слову, поэтому тесты на «пустоту» строятся только на
   пустой БД или пробельном запросе.
3. **`Log.Logger` — глобальный синглтон**: в параллельных тестах инициализируется один раз
   под lock, контейнеру не передаётся во владение (`dispose: false`).
4. Файл `test-rag-*.log` пишется в UTF-8; «кракозябры» в консоли Windows — артефакт кодовой
   страницы терминала, в VS Code кириллица корректна.
5. Пакеты Serilog доступны в тестовой сборке транзитивно через ProjectReference на
   [`otus-projectwork-rag.csproj`](../../otus-projectwork-rag.csproj) (`Serilog.AspNetCore`,
   `Serilog.Sinks.File`) — прямые PackageReference не добавлялись.