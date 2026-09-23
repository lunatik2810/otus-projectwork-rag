
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

Сервер рассчитан на запуск в контейнере: вся конфигурация передаётся переменными окружения
(переопределяют `appsettings.json` через `Section__Key`), папки документов и БД монтируются
томами. Готовые файлы — [`Dockerfile`](Dockerfile), [`docker-compose.yml`](docker-compose.yml),
[`.env.example`](.env.example), [`.dockerignore`](.dockerignore).

### Запуск одной командой

```bash
git clone <repo> && cd <repo>
docker compose up
```
Для моего репозитория:

```bash
git clone https://github.com/lunatik2810/otus-projectwork-rag.git
cd otus-projectwork-rag
docker compose up
```
или одной строкой

```bash
git clone https://github.com/lunatik2810/otus-projectwork-rag.git && cd otus-projectwork-rag && docker compose up
```

После старта MCP-сервер доступен агенту на `http://localhost:6543` — подключение в IDE
настраивается так же, как при локальном запуске (см. выше). Состояние проверяется
`docker compose ps` (healthcheck: `GET /health`).

### Модель multilingual-e5-small (235 МБ) — хранится вне git

ONNX-модель `Resources/multilingual-e5-small/model_O4.onnx` **не лежит в репозитории**:
GitHub отклоняет файлы больше 100 MiB. Модель опубликована как asset GitHub Release
и подтягивается автоматически:

- **Docker** — модель скачивается при `docker compose up` (шаг в
  [`Dockerfile`](Dockerfile), `ARG MODEL_URL`); если файл уже есть в контексте сборки
  (локальная разработка), скачивание пропускается;
- **Локальная разработка (Windows)** — один раз выполните:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\download-model.ps1
```

  или вручную сохраните
  `https://github.com/lunatik2810/otus-projectwork-rag/releases/latest/download/model_O4.onnx`
  в `Resources\multilingual-e5-small\model_O4.onnx`.

Обновление модели: отредактируйте файл, затем во вкладке *Releases* репозитория GitHub
создайте релиз (или обновите существующий) и прикрепите `model_O4.onnx` как asset —
URL `releases/latest/download/model_O4.onnx` останется прежним.

### Где преподаватель указывает свою Ollama

MCP-сервер работает в контейнере, а Ollama — на компьютере преподавателя. Внутри контейнера
`localhost` — это сам контейнер, поэтому адрес хоста задаётся отдельно. Два параметра
настраиваются в [`docker-compose.yml`](docker-compose.yml) (или через `.env`, см.
[`.env.example`](.env.example)):

| Переменная | Значение по умолчанию | Назначение |
|---|---|---|
| `Ollama__BaseUrl` | `http://host.docker.internal:11434/v1` | Адрес OpenAI-совместимого API Ollama на хосте |
| `Ollama__Model` | `qwen2.5:3b` | Модель грейдинга релевантности и расширения запроса |

Чтобы использовать свою модель:

```bash
cp .env.example .env
# отредактировать OLLAMA_MODEL=своя_модель (и при необходимости OLLAMA_BASE_URL=...)
docker compose up
```

Важные нюансы доступа к Ollama на хосте:

- **Windows/macOS (Docker Desktop)** — работает из коробки: `host.docker.internal` ведёт
  на хост, Ollama, слушающая `127.0.0.1:11434`, достижима.
- **Linux** — в compose уже добавлен `extra_hosts: host.docker.internal:host-gateway`,
  но саму Ollama нужно запустить с `OLLAMA_HOST=0.0.0.0` (по умолчанию она слушает только
  `127.0.0.1` и недоступна контейнеру через шлюз).
- **Альтернатива** — поднять Ollama в Docker: раскомментировать сервис `ollama` в
  [`docker-compose.yml`](docker-compose.yml) и задать
  `Ollama__BaseUrl: "http://ollama:11434/v1"`.

### Тома

- `./data:/data` — SQLite `vectorDb.db` (переживает пересоздание контейнера);
- `./docs:/docs` — папка, которую индексируете: `index_folder("/docs", "*.txt")`;
- `./logs:/logs` — файлы логов Serilog.

ONNX-модель multilingual-e5-small (235 МБ) не хранится в git — при сборке образа она
скачивается из GitHub Release (см. раздел выше) либо берётся из контекста, если файл
уже есть локально. Токенизатор и конфиг модели (`tokenizer.json`, `tokenizer_config.json`)
входят в репозиторий. При желании модель можно заменить своим томом через
`Embedding__ModelDirectory`.

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

# Проверочные факты

Можно взять любое досье из папки [DataBaseRAG](Resources\DataBaseRAG), они уникальны.
Например:
 - [dossier_001.txt](Resources\DataBaseRAG\dossier_001.txt) 
ФИО: Миронов Владислав Владиславович
Дата и место рождения: 03.09.1993, Воронеж
Гражданство: Российская Федерация
Паспортные данные: 0000 100001, выдан 15.06.2015 вымышленным подразделением №101
Адрес регистрации: г. Москва, ул. Речная, д. 15, кв. 141
Фактическое проживание: г. Москва, ул. Речная, д. 15, кв. 141, временное проживание не менялось последние 2 года
Телефон: +7 (900) 775-30-55
Email: владислав.миронов1@example.invalid
Социальные сети: Telegram: @владислав_demo_1; VK: vk.com/id700001 (вымышленные профили)

 - [dossier_002.txt](Resources\DataBaseRAG\dossier_002.txt) 
ФИО: Орлов Антон Алексеевич
Дата и место рождения: 21.12.1989, Ярославль
Гражданство: Российская Федерация, второе гражданство отсутствует
Паспортные данные: 0000 100002, выдан 15.01.2005 вымышленным подразделением №102
Адрес регистрации: г. Казань, ул. Молодёжная, д. 81, кв. 77
Фактическое проживание: г. Казань, ул. Молодёжная, д. 81, кв. 77, временное проживание не менялось последние 2 года
Телефон: +7 (900) 525-24-89
Email: антон.орлов2@example.invalid
Социальные сети: Telegram: @антон_demo_2; VK: vk.com/id700002 (вымышленные профили)

...
и т.д.

# Пример использования
1. index_status()
2. index_folder("/app/Resources/DataBaseRAG") - это для контейнера  (под отладкой, то запускала index_folder("Resources\DataBaseRAG"))
3. index_status()
4. find_relevant_docs("Миронов")
5. ask_question("Какой номер телефона Миронов Владислав Владиславович") 

# Конфиг-файл для подключения MCP сервера проекта
## VSCode copilot

Путь к mcp.json - Для Windows глобально C:\Users\{user}\AppData\Roaming\Code\User\mcp.json

Сам файл mcp.json:
```json
{
	"servers": {
		"mcp-server-otus-projectwork-rag": {
			"url": "http://localhost:6543",
			"type": "http"
		}
	},
	"inputs": []
}
```

## Отдельные файлы
Отдельно писала про это в папке [ExampleConfigsMCP](ForTeachers\ExampleConfigsMCP) 

