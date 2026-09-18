использовать официальный C# SDK ModelContextProtocol;
[McpServerTool] для инструментов;
правильное описание Tool;
DI через IServiceCollection;
stdio для локальных MCP;
Streamable HTTP для удалённых/общих MCP;
Stateless для обычного HTTP MCP;
никогда не писать диагностические сообщения в stdout при stdio;
использовать ILogger;
тестировать Tools отдельно;
не смешивать MCP transport с бизнес-логикой;
учитывать особенности .NET 10;
не придумывать API SDK, а сверяться с актуальной документацией.