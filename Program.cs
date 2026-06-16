using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// すべてのログを標準エラーに出力する（標準出力はMCPプロトコルメッセージ用）
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// DIコンテナ登録
// # if DEBUG
    // DbConnectionFactoryのDI注入
    builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
// # else
    // builder.Services.AddSingleton<IDbConnectionFactory, PostgreSqlConnectionFactory>();
// #endif

// MCPサービスを追加 使用するトランスポート（stdio）とツールを設定する
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DatabaseQueryTools>()
    .WithTools<WorkshopTools>();

await builder.Build().RunAsync();
