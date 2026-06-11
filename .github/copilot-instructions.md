## DB操作ルール

- DBへの操作は必ずMCPサーバー経由（PreviewSql → ExecuteSql）で行うこと
- ターミナルから直接DBを操作しないこと
- ExecuteSql を呼び出す前に、必ず実行するSQLをユーザーに提示し、承認を得ること
