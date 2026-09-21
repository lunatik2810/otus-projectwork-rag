# Архитектура

## Индексация

```
Документы → FileScanner (glob + SHA-256) → TextFileReader (BOM/UTF-8/Windows-1251)
  → TextChunker (предложения + переводы строк; 480 токенов; overlap 20)
  → E5SmallEmbedder (passage:, lowercase, mean pooling, L2)
  → SQLite (Documents, Chunks, Embeddings) + FTS5 (BM25) через триггеры
```

## Гибридный поиск (find_relevant_docs)

```
Query → EmbedQueryAsync (query:) → Vector search top-50
      → FTS5 MATCH (экранирование токенов) → BM25 top-50
      → RRF (k=60) → top-10 → JSON (файл, chunkId, vectorScore, bm25Score, rrfScore)
```

## Corrective RAG (ask_question)

```
User Query → Retrieve (hybrid → RRF) → Grade Chunks (Ollama yes/no)
  → релевантных >= MinRelevant(3) → вернуть чанки агенту
  → мало → QueryExpander (Ollama) → повторный Retrieve (до 2 расширений)
```

## Таблицы SQLite (база vectorDb)

- `Documents` — путь (UNIQUE), имя, ContentHash (SHA-256), CreatedAt/UpdatedAt (ISO-8601 UTC).
- `Chunks` — DocumentId (FK), ChunkIndex, Text, TokenCount; UNIQUE(DocumentId, ChunkIndex).
- `Embeddings` — ChunkId (PK/FK), Vector (BLOB float32 LE), Dimensions (384).
- `ChunksFts` — виртуальная FTS5, content='Chunks', content_rowid='Id'.
- Триггеры: `chunks_ai` (INSERT → FTS), `chunks_ad` (DELETE → очистка FTS командой 'delete').

## Ключевые сервисы (DI)

| Сервис | Ответственность |
|---|---|
| `SqliteConnectionFactory` | WAL, внешние ключи, путь БД |
| `DocumentRepository`/`ChunkRepository`/`EmbeddingRepository`/`FtsRepository` | Доступ к данным (соединение+транзакция от вызывающего) |
| `TextChunker` | Разбиение текста (один проход токенизатора) |
| `E5SmallEmbedder` / `TokenizerProvider` | ONNX-инференс, батчинг, query:/passage: |
| `HybridSearchService` | BM25 + вектор → RRF |
| `ChunkGrader` / `QueryExpander` / `CorrectiveRagPipeline` | Ollama-логика Corrective RAG |
| `RagTools` | 4 MCP-инструмента (тонкий слой над MediatR) |

## Особенности модели multilingual-e5-small

- Входы модели: `input_ids`, `attention_mask`, `token_type_ids` (передаётся нулями).
- Выход: `last_hidden_state` → mean pooling по маске внимания → L2-нормализация.
- Нижний регистр + обязательные префиксы `passage: ` / `query: `.
- Паддинг до длины батча (модель принимает динамическую длину до 512).