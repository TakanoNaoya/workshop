using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;

/// <summary>
/// DB接続の生成とタイムアウト設定を一元管理するファクトリ。
/// DatabaseQueryTools・WorkshopTools など複数ツールから共用する。
/// </summary>
internal sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly IConfiguration _configuration;

    public DbConnectionFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    private const string SqlServerProvider  = "sqlserver";
    private const string PostgreSqlProvider = "postgresql";

    /// <summary>
    /// 設定に応じた未オープンの DbConnection を生成して返す。
    /// </summary>
    public DbConnection CreateConnection()
    {
        var provider         = ResolveProvider();
        var connectionString = ResolveConnectionString();

        return provider switch
        {
            SqlServerProvider  => new SqlConnection(connectionString),
            PostgreSqlProvider => new NpgsqlConnection(connectionString),
            _ => throw new InvalidOperationException("Unsupported database provider.")
        };
    }

    /// <summary>
    /// コマンドタイムアウト秒数を返す（環境変数 > 設定値 > 既定値 30秒）。
    /// </summary>
    public int CommandTimeoutSeconds
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("MCP_DB_COMMAND_TIMEOUT_SECONDS");
            if (int.TryParse(fromEnv, out var envVal))
            {
                return Math.Clamp(envVal, 1, 600);
            }

            var fromConfig = _configuration.GetValue<int?>("Database:CommandTimeoutSeconds");
            return Math.Clamp(fromConfig ?? 30, 1, 600);
        }
    }

    private string ResolveProvider()
    {
        var raw = Environment.GetEnvironmentVariable("MCP_DB_PROVIDER")
        ?? _configuration["Database:Provider"]
        ?? SqlServerProvider;

        return raw.Trim().ToLowerInvariant() switch
        {
            "sqlserver" or "mssql"                => SqlServerProvider,
            "postgresql" or "postgres" or "pgsql" => PostgreSqlProvider,
            _ => throw new InvalidOperationException(
                "Unsupported database provider. Use one of: sqlserver, postgresql.")
        };
    }

    private string ResolveConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("MCP_DB_CONNECTION_STRING")
        ?? _configuration["Database:ConnectionString"]
        ?? _configuration.GetConnectionString("Default");

        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException(
                "Database connection string is not configured. Set MCP_DB_CONNECTION_STRING or Database:ConnectionString.");
        }

        return cs;
    }
}
