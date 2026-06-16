using System.ComponentModel;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

internal sealed class DatabaseQueryTools
{
    private static readonly TimeSpan ApprovalTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, PendingApproval> PendingApprovals = new(StringComparer.Ordinal);
    private readonly IDbConnectionFactory _dbFactory;
    private readonly ILogger<DatabaseQueryTools> _logger;
    private const string StatementTypeSelect = "SELECT";
    private const string StatementTypeInsert = "INSERT";
    private const string StatementTypeUpdate = "UPDATE";
    private const string StatementTypeDelete = "DELETE";
    private const string StatementTypeCreate = "CREATE";
    private const string StatementTypeAlter = "ALTER";
    private const string StatementTypeDrop = "DROP";

    private static readonly Regex DisallowedSqlPattern = new(
        @"\b(merge|truncate|exec|execute|grant|revoke|deny)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AllowedStatementPattern = new(
        @"^(select|insert|update|delete|create|alter|drop)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DatabaseQueryTools(IDbConnectionFactory dbFactory, ILogger<DatabaseQueryTools> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    [McpServerTool]
    [Description("SQL文（SELECT/INSERT/UPDATE/DELETE/CREATE/ALTER/DROP）をプレビューし、実行に必要な承認トークンを発行する。")]
    /// <summary>
    /// 実行前にSQL文をプレビューし、承認トークンを発行する。
    /// </summary>
    public async Task<SqlPreviewResult> PreviewSql(
        [Description("実行するSQL文。使用可能なコマンド: SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP。")] string sql,
        [Description("プレビューする最大行数（1〜200）。デフォルト: 50。")] int maxRows = 50)
    {
        _logger.LogInformation("[Tool] PreviewSql called: {SqlFirstLine}", sql.Split('\n')[0].Trim());
        PurgeExpiredApprovals();

        // プレビュー時点で許可された単一SQLかどうかを検証する。
        var normalizedSql = EnsureAllowedSingleStatement(sql);
        var statementType = GetStatementType(normalizedSql);
        var effectiveMaxRows = Math.Clamp(maxRows, 1, 200);
        var columns = new List<string>();
        var rows = new List<Dictionary<string, object?>>();
        if (string.Equals(statementType, StatementTypeSelect, StringComparison.Ordinal))
        {
            await using var connection = _dbFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = normalizedSql;
            command.CommandTimeout = _dbFactory.CommandTimeoutSeconds;

            await using var reader = await command.ExecuteReaderAsync();

            // 列名を先に確定して、行マッピング時に再利用する。
            columns = Enumerable.Range(0, reader.FieldCount)
                .Select(reader.GetName)
                .ToList();

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
        }

        var previewId = Guid.NewGuid().ToString("N");
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(ApprovalTtl);
        // 実行時照合のため、正規化済みSQLと文種別、件数上限をトークンに紐づける。
        PendingApprovals[previewId] = new PendingApproval(normalizedSql, statementType, effectiveMaxRows, expiresAtUtc);

        return new SqlPreviewResult
        {
            PreviewId = previewId,
            ExpiresAtUtc = expiresAtUtc,
            StatementType = statementType,
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            IsTruncated = string.Equals(statementType, StatementTypeSelect, StringComparison.Ordinal)
                && rows.Count == effectiveMaxRows
        };
    }

    [McpServerTool]
    [Description("承認済みのSQL文（SELECT/INSERT/UPDATE/DELETE/CREATE/ALTER/DROP）をSQL ServerまたはPostgreSQLに対して実行する。PreviewSqlで取得したpreviewIdが必要。")]
    /// <summary>
    /// 事前承認済みのSQL文を実行し、SELECTは結果行、更新系は影響行数を返す。
    /// </summary>
    public async Task<SqlQueryResult> ExecuteSql(
        [Description("実行するSQL文。使用可能なコマンド: SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP。")] string sql,
        [Description("PreviewSqlで取得した承認トークン。")] string previewId,
        [Description("返却する最大行数（1〜1000）。デフォルト: 100。")] int maxRows = 100)
    {
        _logger.LogInformation("[Tool] ExecuteSql called: {SqlFirstLine}", sql.Split('\n')[0].Trim());
        PurgeExpiredApprovals();

        // 先にSQLを検証し、許可されない文を実行前に遮断する。
        var normalizedSql = EnsureAllowedSingleStatement(sql);
        var statementType = GetStatementType(normalizedSql);

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

        if (!string.Equals(pending.StatementType, statementType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SQL statement type does not match the approved preview SQL.");
        }

        // 返却件数はサーバー側で安全な範囲に固定する。
        var effectiveMaxRows = Math.Clamp(maxRows, 1, pending.MaxRows);

        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = normalizedSql;
        command.CommandTimeout = _dbFactory.CommandTimeoutSeconds;

        if (string.Equals(statementType, StatementTypeSelect, StringComparison.Ordinal))
        {
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

            return new SqlQueryResult
            {
                StatementType = statementType,
                Columns = columns,
                Rows = rows,
                RowCount = rows.Count,
                RowsAffected = null,
                IsTruncated = rows.Count == effectiveMaxRows
            };
        }

        var affected = await command.ExecuteNonQueryAsync();
        return new SqlQueryResult
        {
            StatementType = statementType,
            Columns = [],
            Rows = [],
            RowCount = 0,
            RowsAffected = affected,
            IsTruncated = false
        };
    }

    /// <summary>
    /// 単一SQL文ポリシーを強制し、許可された文種別以外や複文を拒否する。
    /// </summary>
    private static string EnsureAllowedSingleStatement(string sql)
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

        // 文頭は許可されたSQL種別のみ受け付ける。
        if (!AllowedStatementPattern.IsMatch(trimmed))
        {
            throw new InvalidOperationException("Only SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, and DROP statements are allowed.");
        }

        // 複文実行を防ぐため、文中セミコロンを拒否する。
        if (trimmed.Contains(';'))
        {
            throw new InvalidOperationException("Multiple SQL statements are not allowed.");
        }

        // 更新系・DDL・権限変更などのキーワードを拒否する。
        if (DisallowedSqlPattern.IsMatch(trimmed))
        {
            throw new InvalidOperationException("Statement contains blocked SQL keywords.");
        }

        return trimmed;
    }

    /// <summary>
    /// SQL文の先頭キーワードから種別を判定する。
    /// </summary>
    private static string GetStatementType(string normalizedSql)
    {
        if (normalizedSql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return StatementTypeSelect;
        }

        if (normalizedSql.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
        {
            return StatementTypeInsert;
        }

        if (normalizedSql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            return StatementTypeUpdate;
        }

        if (normalizedSql.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            return StatementTypeDelete;
        }

        if (normalizedSql.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase))
        {
            return StatementTypeCreate;
        }

        if (normalizedSql.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase))
        {
            return StatementTypeAlter;
        }

        if (normalizedSql.StartsWith("DROP", StringComparison.OrdinalIgnoreCase))
        {
            return StatementTypeDrop;
        }

        throw new InvalidOperationException("Unsupported SQL statement type.");
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

    private sealed record PendingApproval(string NormalizedSql, string StatementType, int MaxRows, DateTimeOffset ExpiresAtUtc);
}

internal sealed class SqlPreviewResult
{
    public required string PreviewId { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public required string StatementType { get; init; }

    public required List<string> Columns { get; init; }

    public required List<Dictionary<string, object?>> Rows { get; init; }

    public required int RowCount { get; init; }

    public required bool IsTruncated { get; init; }
}

internal sealed class SqlQueryResult
{
    public required string StatementType { get; init; }

    public required List<string> Columns { get; init; }

    public required List<Dictionary<string, object?>> Rows { get; init; }

    public required int RowCount { get; init; }

    public required int? RowsAffected { get; init; }

    public required bool IsTruncated { get; init; }
}