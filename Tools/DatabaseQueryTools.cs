using System.ComponentModel;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;
using Npgsql;

internal sealed class DatabaseQueryTools(IConfiguration configuration)
{
    private const string SqlServerProvider = "sqlserver";
    private const string PostgreSqlProvider = "postgresql";
    private static readonly TimeSpan ApprovalTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, PendingApproval> PendingApprovals = new(StringComparer.Ordinal);

    private static readonly Regex DisallowedSqlPattern = new(
        @"\b(insert|update|delete|merge|drop|alter|create|truncate|exec|execute|grant|revoke|deny)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [McpServerTool]
    [Description("Previews a SELECT query and issues an approval token required for execution.")]
    /// <summary>
    /// 実行前にSELECTをプレビューし、承認トークンを発行する。
    /// </summary>
    public async Task<SelectPreviewResult> PreviewSelect(
        [Description("SQL query. Must start with SELECT.")] string sql,
        [Description("Maximum number of rows to preview (1-200). Default: 50.")] int maxRows = 50)
    {
        PurgeExpiredApprovals();

        // プレビュー時点でもSELECT専用ポリシーを適用する。
        var normalizedSql = EnsureSelectOnly(sql);
        var effectiveMaxRows = Math.Clamp(maxRows, 1, 200);
        var provider = GetProvider();
        var connectionString = GetConnectionString();
        var timeoutSeconds = GetCommandTimeoutSeconds();

        await using var connection = CreateConnection(provider, connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = normalizedSql;
        command.CommandTimeout = timeoutSeconds;

        await using var reader = await command.ExecuteReaderAsync();

        // 列名を先に確定して、行マッピング時に再利用する。
        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToList();

        var rows = new List<Dictionary<string, object?>>();
        // 指定上限までをプレビューとして読み取る。
        while (rows.Count < effectiveMaxRows && await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[columns[i]] = reader.IsDBNull(i) ? null : NormalizeValue(reader.GetValue(i));
            }

            rows.Add(row);
        }

        var previewId = Guid.NewGuid().ToString("N");
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(ApprovalTtl);
    // 実行時照合のため、正規化済みSQLと件数上限をトークンに紐づける。
        PendingApprovals[previewId] = new PendingApproval(normalizedSql, effectiveMaxRows, expiresAtUtc);

        return new SelectPreviewResult
        {
            PreviewId = previewId,
            ExpiresAtUtc = expiresAtUtc,
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            IsTruncated = rows.Count == effectiveMaxRows
        };
    }

    [McpServerTool]
    [Description("Executes a SELECT query against the configured SQL Server or PostgreSQL database after approval. Requires previewId from PreviewSelect.")]
    /// <summary>
    /// 事前承認済みのSELECT文を実行し、最大件数で制限した表形式の結果を返す。
    /// </summary>
    public async Task<SelectQueryResult> ExecuteSelect(
        [Description("SQL query. Must start with SELECT.")] string sql,
        [Description("Approval token from PreviewSelect.")] string previewId,
        [Description("Maximum number of rows to return (1-1000). Default: 100.")] int maxRows = 100)
    {
        PurgeExpiredApprovals();

        // 先にSQLを検証し、許可されない文を実行前に遮断する。
        var normalizedSql = EnsureSelectOnly(sql);

        if (string.IsNullOrWhiteSpace(previewId))
        {
            throw new ArgumentException("previewId is required. Execute PreviewSelect before ExecuteSelect.", nameof(previewId));
        }

        // 実行前に承認トークンを原子的に消費し、再利用や並行実行を防ぐ。
        if (!PendingApprovals.TryRemove(previewId, out var pending))
        {
            throw new InvalidOperationException("Invalid or expired previewId. Execute PreviewSelect again.");
        }

        if (pending.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("previewId has expired. Execute PreviewSelect again.");
        }

        if (!string.Equals(pending.NormalizedSql, normalizedSql, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SQL does not match the approved preview SQL.");
        }

        // 返却件数はサーバー側で安全な範囲に固定する。
        var effectiveMaxRows = Math.Clamp(maxRows, 1, pending.MaxRows);
        var provider = GetProvider();
        var connectionString = GetConnectionString();
        var timeoutSeconds = GetCommandTimeoutSeconds();

        await using var connection = CreateConnection(provider, connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = normalizedSql;
        command.CommandTimeout = timeoutSeconds;

        await using var reader = await command.ExecuteReaderAsync();

        // 列名を先に確定し、以降の行データで共通利用する。
        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToList();

        var rows = new List<Dictionary<string, object?>>();
        // 指定件数に達するまで1行ずつ読み取る。
        while (rows.Count < effectiveMaxRows && await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[columns[i]] = reader.IsDBNull(i) ? null : NormalizeValue(reader.GetValue(i));
            }

            rows.Add(row);
        }

        return new SelectQueryResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            IsTruncated = rows.Count == effectiveMaxRows
        };
    }

    /// <summary>
    /// 環境変数または設定からDBプロバイダーを取得し、内部で使う値に正規化する。
    /// </summary>
    private string GetProvider()
    {
        // 優先順は環境変数 > 設定値 > 既定値。
        var provider =
            Environment.GetEnvironmentVariable("MCP_DB_PROVIDER")
            ?? configuration["Database:Provider"]
            ?? SqlServerProvider;

        // エイリアス入力を吸収して利用可能な2種類に統一する。
        var normalized = provider.Trim().ToLowerInvariant();
        return normalized switch
        {
            "sqlserver" or "mssql" => SqlServerProvider,
            "postgresql" or "postgres" or "pgsql" => PostgreSqlProvider,
            _ => throw new InvalidOperationException(
                "Unsupported database provider. Use one of: sqlserver, postgresql.")
        };
    }

    /// <summary>
    /// 環境変数または設定から接続文字列を取得し、未設定なら例外にする。
    /// </summary>
    private string GetConnectionString()
    {
        // 優先順は環境変数 > 設定値 > ConnectionStrings:Default。
        var connectionString =
            Environment.GetEnvironmentVariable("MCP_DB_CONNECTION_STRING")
            ?? configuration["Database:ConnectionString"]
            ?? configuration.GetConnectionString("Default");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Database connection string is not configured. Set MCP_DB_CONNECTION_STRING or Database:ConnectionString.");
        }

        return connectionString;
    }

    /// <summary>
    /// コマンドタイムアウト秒数を取得し、許容範囲に丸める（環境変数を優先）。
    /// </summary>
    private int GetCommandTimeoutSeconds()
    {
        var timeoutFromEnv = Environment.GetEnvironmentVariable("MCP_DB_COMMAND_TIMEOUT_SECONDS");
        if (int.TryParse(timeoutFromEnv, out var envTimeout))
        {
            // 極端な値を避けるため上限下限を固定する。
            return Math.Clamp(envTimeout, 1, 600);
        }

        var timeoutFromConfig = configuration.GetValue<int?>("Database:CommandTimeoutSeconds");
        return Math.Clamp(timeoutFromConfig ?? 30, 1, 600);
    }

    /// <summary>
    /// プロバイダーに応じたDbConnectionインスタンスを生成する。
    /// </summary>
    private static DbConnection CreateConnection(string provider, string connectionString)
    {
        // ここで接続種別を切り替え、呼び出し側は共通APIで扱えるようにする。
        return provider switch
        {
            SqlServerProvider => new SqlConnection(connectionString),
            PostgreSqlProvider => new NpgsqlConnection(connectionString),
            _ => throw new InvalidOperationException("Unsupported database provider.")
        };
    }

    /// <summary>
    /// 読み取り専用ポリシーを強制し、単一のSELECT文以外（更新系・DDL・複文）を拒否する。
    /// </summary>
    private static string EnsureSelectOnly(string sql)
    {
        // 空文字やnullは実行対象外。
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("SQL query is required.", nameof(sql));
        }

        var trimmed = sql.Trim();
        // 末尾セミコロンのみは許容し、検証のために除去する。
        if (trimmed.EndsWith(';'))
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        // 文頭はSELECTのみ許可する。
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only SELECT statements are allowed.");
        }

        // 複文実行を防ぐため、文中セミコロンを拒否する。
        if (trimmed.Contains(';'))
        {
            throw new InvalidOperationException("Multiple SQL statements are not allowed.");
        }

        // 更新系・DDL・権限変更などのキーワードを拒否する。
        if (DisallowedSqlPattern.IsMatch(trimmed))
        {
            throw new InvalidOperationException("Only SELECT statements are allowed.");
        }

        return trimmed;
    }

    /// <summary>
    /// 期限切れの承認トークンを破棄する。
    /// </summary>
    private static void PurgeExpiredApprovals()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, approval) in PendingApprovals)
        {
            if (approval.ExpiresAtUtc <= now)
            {
                PendingApprovals.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// 返却しやすい形式に値を正規化する。
    /// </summary>
    private static object? NormalizeValue(object value)
    {
        // バイナリはそのまま返せないためBase64文字列へ変換する。
        return value switch
        {
            byte[] bytes => Convert.ToBase64String(bytes),
            DateTimeOffset dto => dto,
            DateTime dt => dt,
            _ => value
        };
    }

    private sealed record PendingApproval(string NormalizedSql, int MaxRows, DateTimeOffset ExpiresAtUtc);
}

internal sealed class SelectPreviewResult
{
    public required string PreviewId { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public required List<string> Columns { get; init; }

    public required List<Dictionary<string, object?>> Rows { get; init; }

    public required int RowCount { get; init; }

    public required bool IsTruncated { get; init; }
}

internal sealed class SelectQueryResult
{
    public required List<string> Columns { get; init; }

    public required List<Dictionary<string, object?>> Rows { get; init; }

    public required int RowCount { get; init; }

    public required bool IsTruncated { get; init; }
}