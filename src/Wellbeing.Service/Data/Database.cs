using Dapper;
using Microsoft.Data.Sqlite;

namespace Wellbeing.Service.Data;

public sealed class Database
{
    readonly string _connectionString;

    public Database(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 30,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA busy_timeout = 5000;");
        return connection;
    }

    public void Migrate()
    {
        using var connection = Open();
        connection.Execute("PRAGMA journal_mode = WAL;");
        connection.Execute("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);");
        var version = connection.ExecuteScalar<long?>("SELECT MAX(version) FROM schema_version;") ?? 0;

        for (var i = (int)version; i < Migrations.Length; i++)
        {
            using var tx = connection.BeginTransaction();
            connection.Execute(Migrations[i], transaction: tx);
            connection.Execute("INSERT INTO schema_version(version) VALUES (@v);", new { v = i + 1 }, tx);
            tx.Commit();
        }
    }

    static readonly string[] Migrations =
    [
        """
        CREATE TABLE kv_state (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        -- Известные программы
        CREATE TABLE apps (
            exe            TEXT PRIMARY KEY,   -- 'game.exe', нижний регистр
            display_name   TEXT NOT NULL,
            path           TEXT,
            icon_png       BLOB,
            first_seen_utc INTEGER NOT NULL,
            last_seen_utc  INTEGER NOT NULL
        );

        -- Поминутные агрегаты: сколько секунд программа была активна в минуте
        CREATE TABLE usage_minute (
            day        TEXT    NOT NULL,       -- логический день 'YYYY-MM-DD' (с учётом времени сброса)
            minute_utc INTEGER NOT NULL,       -- unix-время начала минуты
            exe        TEXT    NOT NULL,
            seconds    INTEGER NOT NULL,
            PRIMARY KEY (day, minute_utc, exe)
        );
        CREATE INDEX ix_usage_day_exe ON usage_minute(day, exe);

        -- История политики: на день D действует ревизия с max(seq) среди effective_day <= D
        CREATE TABLE policy_revision (
            seq           INTEGER PRIMARY KEY,
            created_utc   INTEGER NOT NULL,
            effective_day TEXT    NOT NULL,
            policy_json   TEXT    NOT NULL,
            summary       TEXT
        );

        -- Отправленные предупреждения 10/5/1 мин (чтобы не повторялись после перезапуска службы)
        CREATE TABLE warning_sent (
            day           TEXT    NOT NULL,
            limit_id      TEXT    NOT NULL,
            threshold_min INTEGER NOT NULL,
            PRIMARY KEY (day, limit_id, threshold_min)
        );

        -- Настройки, не влияющие на учёт (уведомления)
        CREATE TABLE settings (
            key        TEXT PRIMARY KEY,
            value_json TEXT NOT NULL
        );

        -- Журнал: блокировки, перезапуски агента, работа без агента, изменения лимитов
        CREATE TABLE event_log (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            ts_utc    INTEGER NOT NULL,
            kind      TEXT    NOT NULL,
            message   TEXT    NOT NULL
        );
        CREATE INDEX ix_event_log_ts ON event_log(ts_utc);
        """,
    ];
}
