using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace SkyNetwork.Site.Data;

/// <summary>
/// SQLite storage. The <c>members</c> table is the FSD server's own account table (same file, same
/// schema), so a member registered on the site can connect to the network with the same CID and
/// password. Everything else belongs to the site.
/// </summary>
public sealed class Database
{
    private readonly string _connectionString;

    static Database() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    public Database(IOptions<SiteOptions> options)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.Value.Database,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        c.Execute("PRAGMA busy_timeout = 3000;");
        return c;
    }

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public void Migrate()
    {
        using var c = Open();
        c.Execute("PRAGMA journal_mode = WAL;");
        c.Execute("""
            -- Shared with skynet-fsd / skynet-admin: do not change these columns.
            CREATE TABLE IF NOT EXISTS members (
                cid INTEGER PRIMARY KEY, name TEXT NOT NULL,
                rating INTEGER NOT NULL DEFAULT 1,
                salt BLOB NOT NULL, hash BLOB NOT NULL,
                suspended INTEGER NOT NULL DEFAULT 0);

            CREATE TABLE IF NOT EXISTS member_profiles (
                cid INTEGER PRIMARY KEY,
                email TEXT UNIQUE,
                country TEXT NOT NULL DEFAULT '',
                registered_at INTEGER NOT NULL,
                last_login_at INTEGER,
                suspension_reason TEXT NOT NULL DEFAULT '');

            CREATE TABLE IF NOT EXISTS staff_roles (
                cid INTEGER NOT NULL, role TEXT NOT NULL, PRIMARY KEY (cid, role));

            CREATE TABLE IF NOT EXISTS staff_notes (
                id INTEGER PRIMARY KEY AUTOINCREMENT, cid INTEGER NOT NULL, author_cid INTEGER NOT NULL,
                body TEXT NOT NULL, created_at INTEGER NOT NULL);

            CREATE TABLE IF NOT EXISTS audit_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT, actor_cid INTEGER NOT NULL, action TEXT NOT NULL,
                target TEXT NOT NULL DEFAULT '', details TEXT NOT NULL DEFAULT '', created_at INTEGER NOT NULL);

            CREATE TABLE IF NOT EXISTS flight_plans (
                id INTEGER PRIMARY KEY AUTOINCREMENT, cid INTEGER NOT NULL, callsign TEXT NOT NULL,
                rules TEXT NOT NULL, aircraft TEXT NOT NULL, cruise_speed INTEGER NOT NULL,
                departure TEXT NOT NULL, destination TEXT NOT NULL, alternate TEXT NOT NULL DEFAULT '',
                departure_time TEXT NOT NULL, cruise_altitude TEXT NOT NULL,
                enroute_minutes INTEGER NOT NULL, fuel_minutes INTEGER NOT NULL,
                route TEXT NOT NULL, remarks TEXT NOT NULL DEFAULT '', created_at INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_flight_plans_cid ON flight_plans (cid, id);

            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, summary TEXT NOT NULL DEFAULT '',
                body TEXT NOT NULL DEFAULT '', airports TEXT NOT NULL DEFAULT '',
                starts_at INTEGER NOT NULL, ends_at INTEGER NOT NULL,
                published INTEGER NOT NULL DEFAULT 1, created_by INTEGER NOT NULL, created_at INTEGER NOT NULL);

            CREATE TABLE IF NOT EXISTS news (
                id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, body TEXT NOT NULL,
                published INTEGER NOT NULL DEFAULT 1, author_cid INTEGER NOT NULL, created_at INTEGER NOT NULL);

            CREATE TABLE IF NOT EXISTS bookings (
                id INTEGER PRIMARY KEY AUTOINCREMENT, cid INTEGER NOT NULL, callsign TEXT NOT NULL,
                starts_at INTEGER NOT NULL, ends_at INTEGER NOT NULL, created_at INTEGER NOT NULL);

            CREATE TABLE IF NOT EXISTS network_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT, cid INTEGER NOT NULL, callsign TEXT NOT NULL,
                kind TEXT NOT NULL, details TEXT NOT NULL DEFAULT '',
                started_at INTEGER NOT NULL, ended_at INTEGER);
            CREATE INDEX IF NOT EXISTS ix_sessions_cid ON network_sessions (cid, started_at);

            CREATE TABLE IF NOT EXISTS tickets (
                id INTEGER PRIMARY KEY AUTOINCREMENT, cid INTEGER, email TEXT NOT NULL DEFAULT '',
                subject TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'open',
                created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL);

            CREATE TABLE IF NOT EXISTS ticket_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT, ticket_id INTEGER NOT NULL REFERENCES tickets(id),
                cid INTEGER, staff INTEGER NOT NULL DEFAULT 0, body TEXT NOT NULL, created_at INTEGER NOT NULL);

            CREATE TABLE IF NOT EXISTS training_requests (
                id INTEGER PRIMARY KEY AUTOINCREMENT, cid INTEGER NOT NULL, target_rating INTEGER NOT NULL,
                message TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT 'open',
                instructor_cid INTEGER, staff_comment TEXT NOT NULL DEFAULT '',
                created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL);
            """);
        // Staff ranks (SUP, ADM) used to live in members.rating; they get their own column. The FSD
        // server does the same migration: whichever starts first moves them, in one transaction.
        using (var tx = c.BeginTransaction(System.Data.IsolationLevel.Serializable))
        {
            var columns = c.Query<string>("SELECT name FROM pragma_table_info('members')", transaction: tx);
            if (!columns.Contains("staff_rank", StringComparer.OrdinalIgnoreCase))
            {
                c.Execute("ALTER TABLE members ADD COLUMN staff_rank INTEGER NOT NULL DEFAULT 0", transaction: tx);
                c.Execute("UPDATE members SET staff_rank = rating, rating = 1 WHERE rating >= 11", transaction: tx);
            }
            tx.Commit();
        }

        // Columns added after the first release.
        AddColumn(c, "member_profiles", "suspended_until", "INTEGER");
        AddColumn(c, "member_profiles", "pilot_rating", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(c, "member_profiles", "military_rating", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(c, "training_requests", "track", "TEXT NOT NULL DEFAULT 'atc'");
        AddColumn(c, "flight_plans", "waypoints", "TEXT NOT NULL DEFAULT ''");

        // Waypoints and airway segments learned from imported SimBrief routes (see NavData).
        c.Execute("""
            CREATE TABLE IF NOT EXISTS nav_fixes (
                ident TEXT NOT NULL, lat REAL NOT NULL, lon REAL NOT NULL, seen_at INTEGER NOT NULL,
                PRIMARY KEY (ident, lat, lon));
            CREATE TABLE IF NOT EXISTS nav_airways (
                name TEXT NOT NULL, a TEXT NOT NULL, a_lat REAL NOT NULL, a_lon REAL NOT NULL,
                b TEXT NOT NULL, b_lat REAL NOT NULL, b_lon REAL NOT NULL, seen_at INTEGER NOT NULL,
                PRIMARY KEY (name, a, b));
            """);
        AddColumn(c, "member_profiles", "simbrief", "TEXT NOT NULL DEFAULT ''");
        AddColumn(c, "events", "banner", "TEXT NOT NULL DEFAULT ''");
        AddColumn(c, "news", "banner", "TEXT NOT NULL DEFAULT ''");
    }

    private static void AddColumn(SqliteConnection c, string table, string column, string type)
    {
        var columns = c.Query<string>($"SELECT name FROM pragma_table_info('{table}')");
        if (!columns.Contains(column, StringComparer.OrdinalIgnoreCase))
            c.Execute($"ALTER TABLE {table} ADD COLUMN {column} {type}");
    }
}
