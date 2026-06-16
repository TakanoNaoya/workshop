using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

/// <summary>
/// ワークショップ参加者が自由にMCPツールを追加するためのクラス。
/// 在庫アラートツールを完成例として実装済み。
/// 新しいツールはこのクラスにメソッドとして追加する。
/// </summary>
internal sealed class WorkshopTools
{
    private readonly IDbConnectionFactory _dbFactory;
    private readonly ILogger<WorkshopTools> _logger;

    public WorkshopTools(IDbConnectionFactory dbFactory, ILogger<WorkshopTools> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // 完成例: 在庫アラートツール
    // -------------------------------------------------------------------------

    [McpServerTool]
    [Description("在庫数がアラート閾値以下のアイテムを一覧で返す。")]
    public async Task<List<StockAlertItem>> GetStockAlerts()
    {
        _logger.LogInformation("[Tool] GetStockAlerts called");
        const string sql = """
            SELECT
                i.item_id,
                i.item_nm,
                inv.stock_quantity,
                i.alert_threshold,
                i.unit,
                g.item_group_nm
            FROM m_items i
            JOIN t_inventory inv ON inv.item_id = i.item_id
            JOIN m_item_group g  ON g.item_group_id = i.item_group_id
            WHERE i.del_flg = '0'
              AND i.alert_threshold IS NOT NULL
              AND inv.stock_quantity <= i.alert_threshold
            ORDER BY inv.stock_quantity ASC, i.item_id ASC
            """;

        var results = new List<StockAlertItem>();

        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new StockAlertItem
            {
                ItemId         = reader.GetInt32(0),
                ItemName       = reader.GetString(1),
                StockQuantity  = reader.GetInt64(2),
                AlertThreshold = reader.GetInt64(3),
                Unit           = reader.GetString(4),
                ItemGroupName  = reader.GetString(5),
            });
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // TODO: ここに新しいツールを追加する
    //
    // 追加手順:
    //   1. [McpServerTool] 属性を付けた public async Task<T> メソッドを追加する
    //   2. [Description("...")] でツールの説明を日本語で書く
    //   3. dotnet run でMCPサーバーを再起動するとツールが認識される
    //
    // 実装例:
    //
    //   [McpServerTool]
    //   [Description("商品IDを指定して在庫数を取得する。")]
    //   public async Task<long> GetStockQuantity(
    //       [Description("商品ID")] int itemId)
    //   {
    //       // TODO: 実装
    //       throw new NotImplementedException();
    //   }
    // -------------------------------------------------------------------------
}

// -------------------------------------------------------------------------
// 結果型
// -------------------------------------------------------------------------

internal sealed class StockAlertItem
{
    public required int    ItemId         { get; init; }
    public required string ItemName       { get; init; }
    public required long   StockQuantity  { get; init; }
    public required long   AlertThreshold { get; init; }
    public required string Unit           { get; init; }
    public required string ItemGroupName  { get; init; }
}
