# MCP Server

This README was created using the C# MCP server project template.
It demonstrates how you can easily create an MCP server using C# and publish it as a NuGet package.

The MCP server is built as a self-contained application and does not require the .NET runtime to be installed on the target machine.
However, since it is self-contained, it must be built for each target platform separately.
By default, the template is configured to build for:
* `win-x64`
* `win-arm64`
* `osx-arm64`
* `linux-x64`
* `linux-arm64`
* `linux-musl-x64`

If your users require more platforms to be supported, update the list of runtime identifiers in the project's `<RuntimeIdentifiers />` element.

See [aka.ms/nuget/mcp/guide](https://aka.ms/nuget/mcp/guide) for the full guide.

Please note that this template is currently in an early preview stage. If you have feedback, please take a [brief survey](http://aka.ms/dotnet-mcp-template-survey).

## Checklist before publishing to NuGet.org

- Test the MCP server locally using the steps below.
- Update the package metadata in the .csproj file, in particular the `<PackageId>`.
- Update `.mcp/server.json` to declare your MCP server's inputs.
  - See [configuring inputs](https://aka.ms/nuget/mcp/guide/configuring-inputs) for more details.
- Pack the project using `dotnet pack`.

The `bin/Release` directory will contain the package file (.nupkg), which can be [published to NuGet.org](https://learn.microsoft.com/nuget/nuget-org/publish-a-package).

## SQLite Tools

This project includes SQLite MCP tools for `db/study.db`:

- `PreviewSqlite`:
  - First step (required)
  - Creates a preview token (`previewId`) for one SQL statement
  - For `SELECT` / `WITH` / `PRAGMA`, returns preview rows (up to `maxRows`)
  - Preview token expires in 10 minutes
- `ExecuteSqliteGeneric`:
  - Allowed: `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `CREATE`, `ALTER`, `DROP`, `PRAGMA`, `WITH`
  - Single statement only
  - Requires `previewId` from `PreviewSqlite`
  - SQL text must exactly match the previewed SQL
  - `DROP` requires `allowDestructive=true`

This enforces an interactive and safe workflow: preview first, then execute.

## Developing locally

To test this MCP server from source code (locally) without using a built MCP server package, you can configure your IDE to run the project directly using `dotnet run`.

```json
{
  "servers": {
    "MCPServer": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "<PATH TO PROJECT DIRECTORY>"
      ]
    }
  }
}
```

Refer to the VS Code or Visual Studio documentation for more information on configuring and using MCP servers:

- [Use MCP servers in VS Code (Preview)](https://code.visualstudio.com/docs/copilot/chat/mcp-servers)
- [Use MCP servers in Visual Studio (Preview)](https://learn.microsoft.com/visualstudio/ide/mcp-servers)

## Testing the MCP Server

Once configured, you can ask Copilot Chat for a random number, for example, `Give me 3 random numbers`. It should prompt you to use the `get_random_number` tool on the `MCPServer` MCP server and show you the results.

## Database SELECT Tool

This project includes a database tool named `execute_select`.

- It executes SQL Server or PostgreSQL queries.
- It allows `SELECT` statements only.
- `INSERT`, `UPDATE`, `DELETE`, DDL, `EXEC`, and multi-statement SQL are rejected.

## 勉強会向け: PostgreSQLをDockerで立ててToolで操作する

このリポジトリには、勉強会ですぐ使える PostgreSQL の `docker-compose.yml` と初期データ投入SQLが含まれています。

- Compose定義: `docker-compose.yml`
- 初期化SQL: `docker/postgres/init/01_schema_and_seed.sql`

### 1. PostgreSQLを起動

```powershell
docker compose up -d
docker compose ps
```

補足:
- 初回起動時に `docker/postgres/init/01_schema_and_seed.sql` が自動実行されます。
- 2回目以降は既存ボリュームを使うため初期化SQLは再実行されません。
- A5M2互換のため、初期化時のホスト認証方式は `md5` です（`POSTGRES_INITDB_ARGS`）。

### A5M2 接続設定例

- ホスト: `localhost`
- ポート: `15432`
- データベース: `mcp_study`
- ユーザー: `mcp_user`
- パスワード: `mcp_pass`
- SSL: 無効

### 2. MCPサーバー用の環境変数を設定

```powershell
$env:MCP_DB_PROVIDER = "postgresql"
$env:MCP_DB_CONNECTION_STRING = "Host=localhost;Port=15432;Database=mcp_study;Username=mcp_user;Password=mcp_pass;SSL Mode=Disable"
$env:MCP_DB_COMMAND_TIMEOUT_SECONDS = "30"
```

### 3. MCPサーバーを起動

```powershell
dotnet run --project .
```

### 4. MCPツールを使って在庫を確認

MCPクライアント（Copilot Chatなど）から `execute_select` を呼び出し、次のようなSQLを実行します。

在庫一覧:

```sql
SELECT i.item_id, i.item_nm, i.quantity, i.unit, g.item_group_nm
FROM m_items i
JOIN m_item_group g ON g.item_group_id = i.item_group_id
WHERE i.del_flg = '0'
ORDER BY i.item_id
```

しきい値以下の在庫:

```sql
SELECT item_id, item_nm, quantity, alert_threshold, unit
FROM m_items
WHERE del_flg = '0'
  AND alert_threshold IS NOT NULL
  AND quantity <= alert_threshold
ORDER BY quantity ASC, item_id ASC
```

### 5. データを初期状態に戻したい場合

```powershell
docker compose down -v
docker compose up -d
```

`-v` を付けると永続ボリュームを削除するため、次回起動時に初期化SQLが再実行されます。

認証方式を変更した後に反映されない場合も、同様に `down -v` してから再起動してください。

### Externalize connection settings

Do not hard-code connection information. Provide it from outside the code through environment variables or configuration.

#### Option 1: Environment variables (recommended)

- `MCP_DB_PROVIDER` (`sqlserver` or `postgresql`, default: `sqlserver`)
- `MCP_DB_CONNECTION_STRING`
- `MCP_DB_COMMAND_TIMEOUT_SECONDS` (optional, default: 30)

PowerShell example (SQL Server):

```powershell
$env:MCP_DB_PROVIDER = "sqlserver"
$env:MCP_DB_CONNECTION_STRING = "Server=localhost;Database=SampleDb;User Id=app;Password=***;TrustServerCertificate=true"
$env:MCP_DB_COMMAND_TIMEOUT_SECONDS = "30"
dotnet run --project .
```

PowerShell example (PostgreSQL):

```powershell
$env:MCP_DB_PROVIDER = "postgresql"
$env:MCP_DB_CONNECTION_STRING = "Host=localhost;Port=5432;Database=sampledb;Username=app;Password=***;SSL Mode=Disable"
$env:MCP_DB_COMMAND_TIMEOUT_SECONDS = "30"
dotnet run --project .
```

#### Option 2: Configuration file

Use `appsettings.json` (or `appsettings.Development.json`) with:

```json
{
  "Database": {
    "Provider": "sqlserver",
    "ConnectionString": "Server=localhost;Database=SampleDb;User Id=app;Password=***;TrustServerCertificate=true",
    "CommandTimeoutSeconds": 30
  }
}
```

## Publishing to NuGet.org

1. Run `dotnet pack -c Release` to create the NuGet package
2. Publish to NuGet.org with `dotnet nuget push bin/Release/*.nupkg --api-key <your-api-key> --source https://api.nuget.org/v3/index.json`

## Using the MCP Server from NuGet.org

Once the MCP server package is published to NuGet.org, you can configure it in your preferred IDE. Both VS Code and Visual Studio use the `dnx` command to download and install the MCP server package from NuGet.org.

- **VS Code**: Create a `<WORKSPACE DIRECTORY>/.vscode/mcp.json` file
- **Visual Studio**: Create a `<SOLUTION DIRECTORY>\.mcp.json` file

For both VS Code and Visual Studio, the configuration file uses the following server definition:

```json
{
  "servers": {
    "MCPServer": {
      "type": "stdio",
      "command": "dnx",
      "args": [
        "<your package ID here>",
        "--version",
        "<your package version here>",
        "--yes"
      ]
    }
  }
}
```

## More information

.NET MCP servers use the [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) C# SDK. For more information about MCP:

- [Official Documentation](https://modelcontextprotocol.io/)
- [Protocol Specification](https://spec.modelcontextprotocol.io/)
- [GitHub Organization](https://github.com/modelcontextprotocol)
- [MCP C# SDK](https://modelcontextprotocol.github.io/csharp-sdk)
