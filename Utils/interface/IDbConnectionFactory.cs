using System.Data.Common;
/// <summary>
/// DB接続の生成とタイムアウト設定を一元管理するファクトリ。
/// DatabaseQueryTools・WorkshopTools など複数ツールから共用する。
/// </summary>
internal interface IDbConnectionFactory
{
    /// <summary>
    /// 設定に応じた未オープンの DbConnection を生成して返す。
    /// </summary>
    DbConnection CreateConnection();

    /// <summary>
    /// コマンドタイムアウト秒数を返す（環境変数 > 設定値 > 既定値 30秒）。
    /// </summary>
    int CommandTimeoutSeconds { get; }
}