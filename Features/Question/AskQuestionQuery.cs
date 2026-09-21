using MediatR;
using OtusProjectworkRag.Contracts;
using OtusProjectworkRag.Infrastructure.Rag;

namespace OtusProjectworkRag.Features.Question;

/// <summary>
/// Вопрос к базе знаний: полный Corrective RAG-цикл (гибридный поиск + грейдинг
/// релевантности через Ollama + расширение запроса при нехватке релевантных чанков).
/// Результат — отобранные чанки с метаданными; ответ формирует агент-хост.
/// </summary>
public sealed record AskQuestionQuery(string Question) : IRequest<AskQuestionResult>;

/// <summary>Обработчик вопроса к базе знаний.</summary>
public sealed class AskQuestionQueryHandler(
    ICorrectiveRagPipeline pipeline) : IRequestHandler<AskQuestionQuery, AskQuestionResult>
{
    public Task<AskQuestionResult> Handle(AskQuestionQuery request, CancellationToken cancellationToken)
        => pipeline.AskAsync(request.Question, cancellationToken);
}