using Microsoft.Data.Sqlite;
using RetroHub.Core.Models;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Library;

public sealed record DuplicateRom(string Path, string KeptPath);

public sealed record GameCollection(long Id, string Name, int Count);

public sealed record PlaySession(DateTimeOffset StartedAt, long Seconds, string SystemId, string Title, long GameId);

public sealed record UpsertResult(int Added, int Updated, int Moved, IReadOnlyList<DuplicateRom> Duplicates);

/// <summary>Biblioteca em SQLite. Datas são gravadas como segundos Unix (UTC).</summary>
public sealed class LibraryDatabase
{
    private const string GameColumns =
        "id, system_id, title, path, serial, crc32, size_bytes, cover_path, metadata_json, " +
        "favorite, play_seconds, last_played, added_at, emulator_override, source";

    private const string SchemaV1 = """
        CREATE TABLE games (
            id                INTEGER PRIMARY KEY AUTOINCREMENT,
            system_id         TEXT    NOT NULL,
            title             TEXT    NOT NULL,
            path              TEXT    NOT NULL UNIQUE COLLATE NOCASE,
            serial            TEXT,
            crc32             TEXT,
            size_bytes        INTEGER NOT NULL DEFAULT 0,
            cover_path        TEXT,
            metadata_json     TEXT,
            favorite          INTEGER NOT NULL DEFAULT 0,
            play_seconds      INTEGER NOT NULL DEFAULT 0,
            last_played       INTEGER,
            added_at          INTEGER NOT NULL,
            emulator_override TEXT,
            source            TEXT    NOT NULL DEFAULT 'scan'
        );
        CREATE INDEX ix_games_system ON games(system_id);

        CREATE TABLE play_sessions (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            game_id    INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
            started_at INTEGER NOT NULL,
            seconds    INTEGER NOT NULL
        );

        CREATE TABLE rom_folders (
            path     TEXT PRIMARY KEY COLLATE NOCASE,
            added_at INTEGER NOT NULL
        );

        CREATE TABLE installed_emulators (
            id              TEXT PRIMARY KEY,
            version         TEXT    NOT NULL,
            install_dir     TEXT    NOT NULL,
            executable_path TEXT    NOT NULL,
            sha256          TEXT,
            verified        INTEGER NOT NULL DEFAULT 0,
            installed_at    INTEGER NOT NULL
        );

        CREATE TABLE settings (
            key   TEXT PRIMARY KEY,
            value TEXT
        );
        """;

    /// <summary>v2: impressão digital do conteúdo, para reconhecer o mesmo jogo em pastas diferentes.</summary>
    private const string SchemaV2 = """
        ALTER TABLE games ADD COLUMN fingerprint TEXT;
        CREATE INDEX ix_games_fingerprint ON games(system_id, fingerprint);
        """;

    /// <summary>v3: arquivos que o usuário tirou da biblioteca — o escaneamento não os traz de volta.</summary>
    private const string SchemaV3 = """
        CREATE TABLE excluded_paths (
            path     TEXT PRIMARY KEY COLLATE NOCASE,
            added_at INTEGER NOT NULL
        );
        """;

    /// <summary>v4: coleções do usuário ("Zerados", "Quero jogar"…).</summary>
    private const string SchemaV4 = """
        CREATE TABLE collections (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            name       TEXT NOT NULL UNIQUE COLLATE NOCASE,
            created_at INTEGER NOT NULL
        );
        CREATE TABLE collection_games (
            collection_id INTEGER NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
            game_id       INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
            PRIMARY KEY (collection_id, game_id)
        );
        INSERT INTO collections (name, created_at) VALUES ('Zerados', CAST(strftime('%s','now') AS INTEGER)), ('Quero jogar', CAST(strftime('%s','now') AS INTEGER));
        """;

    private readonly string _connectionString;

    public LibraryDatabase(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public void Initialize()
    {
        using var c = Open();
        Execute(c, "PRAGMA journal_mode = WAL;", null);
        var version = Convert.ToInt32(Scalar(c, "PRAGMA user_version;", null));
        if (version < 1)
        {
            using var tx = c.BeginTransaction();
            Execute(c, SchemaV1, tx);
            Execute(c, "PRAGMA user_version = 1;", tx);
            tx.Commit();
        }
        if (version < 2)
        {
            using var tx = c.BeginTransaction();
            Execute(c, SchemaV2, tx);
            Execute(c, "PRAGMA user_version = 2;", tx);
            tx.Commit();
        }
        if (version < 3)
        {
            using var tx = c.BeginTransaction();
            Execute(c, SchemaV3, tx);
            Execute(c, "PRAGMA user_version = 3;", tx);
            tx.Commit();
        }
        if (version < 4)
        {
            using var tx = c.BeginTransaction();
            Execute(c, SchemaV4, tx);
            Execute(c, "PRAGMA user_version = 4;", tx);
            tx.Commit();
        }
    }

    // ── Jogos ───────────────────────────────────────────────────────────────

    public List<Game> GetGames()
    {
        using var c = Open();
        using var cmd = Command(c, $"SELECT {GameColumns} FROM games ORDER BY title COLLATE NOCASE;", null);
        using var r = cmd.ExecuteReader();
        var games = new List<Game>();
        while (r.Read())
            games.Add(ReadGame(r));
        return games;
    }

    public Game? GetGame(long id)
    {
        using var c = Open();
        using var cmd = Command(c, $"SELECT {GameColumns} FROM games WHERE id = $id;", null, ("$id", id));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadGame(r) : null;
    }

    /// <summary>
    /// Grava o resultado de um escaneamento:
    ///   • caminho já conhecido → atualiza;
    ///   • mesmo conteúdo (impressão digital) num caminho que ainda existe → cópia duplicada, ignora;
    ///   • mesmo conteúdo num caminho que sumiu → o arquivo foi movido, atualiza o caminho (mantém tempo jogado e favorito);
    ///   • senão → jogo novo.
    /// No fim, unifica duplicatas que já estavam na biblioteca.
    /// </summary>
    public UpsertResult UpsertScanned(IEnumerable<ScannedRom> roms, string source = "scan")
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var excluded = ExcludedPaths(c, tx);
        var list = roms.Where(r => !excluded.Contains(r.Path)).ToList();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int added = 0, updated = 0, moved = 0;
        var duplicates = new List<DuplicateRom>();

        // 1º: caminhos já conhecidos (assim as impressões digitais antigas ficam preenchidas antes da comparação).
        var pending = new List<ScannedRom>();
        foreach (var rom in list)
        {
            var existing = Scalar(c, "SELECT id FROM games WHERE path = $path;", tx, ("$path", rom.Path));
            if (existing is null)
            {
                pending.Add(rom);
                continue;
            }
            Execute(c, """
                UPDATE games SET size_bytes = $size,
                                 serial = COALESCE($serial, serial),
                                 crc32 = COALESCE($crc, crc32),
                                 fingerprint = COALESCE($fp, fingerprint)
                WHERE id = $id;
                """, tx,
                ("$size", rom.SizeBytes), ("$serial", rom.Serial), ("$crc", rom.Crc32), ("$fp", rom.Fingerprint), ("$id", existing));
            updated++;
        }

        // 2º: caminhos novos.
        foreach (var rom in pending)
        {
            if (rom.Fingerprint is not null && FindByFingerprint(c, tx, rom.SystemId, rom.Fingerprint) is { } same)
            {
                if (File.Exists(same.Path))
                {
                    duplicates.Add(new DuplicateRom(rom.Path, same.Path));
                }
                else
                {
                    Execute(c, "UPDATE games SET path = $path, size_bytes = $size WHERE id = $id;", tx,
                        ("$path", rom.Path), ("$size", rom.SizeBytes), ("$id", same.Id));
                    moved++;
                }
                continue;
            }

            Execute(c, """
                INSERT INTO games (system_id, title, path, serial, crc32, size_bytes, added_at, source, fingerprint)
                VALUES ($system, $title, $path, $serial, $crc, $size, $now, $source, $fp);
                """, tx,
                ("$system", rom.SystemId), ("$title", rom.Title), ("$path", rom.Path), ("$serial", rom.Serial),
                ("$crc", rom.Crc32), ("$size", rom.SizeBytes), ("$now", now), ("$source", source), ("$fp", rom.Fingerprint));
            added++;
        }

        duplicates.AddRange(MergeDuplicates(c, tx));
        tx.Commit();
        return new UpsertResult(added, updated, moved, duplicates);
    }

    private static (long Id, string Path)? FindByFingerprint(SqliteConnection c, SqliteTransaction tx, string systemId, string fingerprint)
    {
        using var cmd = Command(c, "SELECT id, path FROM games WHERE system_id = $s AND fingerprint = $fp ORDER BY id LIMIT 1;", tx,
            ("$s", systemId), ("$fp", fingerprint));
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt64(0), r.GetString(1)) : null;
    }

    /// <summary>
    /// Une registros com o mesmo conteúdo: fica o mais jogado (depois favorito, depois o mais antigo),
    /// somando tempo jogado e sessões. Só mexe no banco — nenhum arquivo é apagado.
    /// </summary>
    private static List<DuplicateRom> MergeDuplicates(SqliteConnection c, SqliteTransaction tx)
    {
        var rows = new List<(long Id, string System, string Fp, string Path, bool Fav, long Secs, long? Last, string? Cover)>();
        using (var cmd = Command(c, """
            SELECT id, system_id, fingerprint, path, favorite, play_seconds, last_played, cover_path
            FROM games
            WHERE fingerprint IS NOT NULL AND (system_id, fingerprint) IN
                  (SELECT system_id, fingerprint FROM games WHERE fingerprint IS NOT NULL
                   GROUP BY system_id, fingerprint HAVING COUNT(*) > 1);
            """, tx))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                rows.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0, r.GetInt64(5),
                          r.IsDBNull(6) ? null : r.GetInt64(6), r.IsDBNull(7) ? null : r.GetString(7)));
        }

        var removed = new List<DuplicateRom>();
        foreach (var group in rows.GroupBy(x => (x.System, x.Fp)))
        {
            var ordered = group.OrderByDescending(x => x.Secs).ThenByDescending(x => x.Fav).ThenBy(x => x.Id).ToList();
            var keep = ordered[0];
            foreach (var extra in ordered.Skip(1))
            {
                Execute(c, "UPDATE play_sessions SET game_id = $keep WHERE game_id = $extra;", tx, ("$keep", keep.Id), ("$extra", extra.Id));
                Execute(c, "DELETE FROM games WHERE id = $extra;", tx, ("$extra", extra.Id));
                removed.Add(new DuplicateRom(extra.Path, keep.Path));
            }
            Execute(c, """
                UPDATE games SET play_seconds = $secs, favorite = $fav, last_played = $last,
                                 cover_path = COALESCE(cover_path, $cover)
                WHERE id = $keep;
                """, tx,
                ("$secs", group.Sum(x => x.Secs)), ("$fav", group.Any(x => x.Fav) ? 1 : 0),
                ("$last", group.Max(x => x.Last)), ("$cover", group.Select(x => x.Cover).FirstOrDefault(x => x is not null)),
                ("$keep", keep.Id));
        }
        return removed;
    }
    /// <summary>Tira o jogo da biblioteca e lembra do caminho para o escaneamento não trazê-lo de volta.</summary>
    public void ExcludeGame(long gameId)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Execute(c, """
            INSERT OR IGNORE INTO excluded_paths (path, added_at)
            SELECT path, $now FROM games WHERE id = $id;
            """, tx, ("$id", gameId), ("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        Execute(c, "DELETE FROM games WHERE id = $id;", tx, ("$id", gameId));
        tx.Commit();
    }

    public int ExcludedCount()
    {
        using var c = Open();
        return Convert.ToInt32(Scalar(c, "SELECT COUNT(*) FROM excluded_paths;", null));
    }

    /// <summary>
    /// Remove jogos cujo arquivo sumiu — mas só se a pasta de ROMs onde ele estava continua acessível
    /// (HD externo desconectado não apaga a biblioteca nem o tempo jogado).
    /// </summary>
    public List<string> RemoveMissingGames(IReadOnlyList<string> romFolders)
    {
        var available = romFolders.Where(Directory.Exists).Select(f => Path.GetFullPath(f).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).ToList();
        var missing = GetGames()
            .Where(g => !File.Exists(g.Path) && available.Any(root => g.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            .Select(g => g.Path)
            .ToList();
        RemoveGamesByPath(missing);
        return missing;
    }

    /// <summary>Zera jogos, sessões e exclusões. Pastas, emuladores e configurações ficam.</summary>
    public void ClearLibrary()
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Execute(c, "DELETE FROM play_sessions;", tx);
        Execute(c, "DELETE FROM games;", tx);
        Execute(c, "DELETE FROM excluded_paths;", tx);
        tx.Commit();
    }

    private static HashSet<string> ExcludedPaths(SqliteConnection c, SqliteTransaction tx)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = Command(c, "SELECT path FROM excluded_paths;", tx);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            set.Add(r.GetString(0));
        return set;
    }

    // ── Coleções ────────────────────────────────────────────────────────────

    public List<GameCollection> GetCollections()
    {
        using var c = Open();
        using var cmd = Command(c, """
            SELECT c.id, c.name, COUNT(cg.game_id)
            FROM collections c LEFT JOIN collection_games cg ON cg.collection_id = c.id
            GROUP BY c.id ORDER BY c.created_at, c.id;
            """, null);
        using var r = cmd.ExecuteReader();
        var list = new List<GameCollection>();
        while (r.Read())
            list.Add(new GameCollection(r.GetInt64(0), r.GetString(1), r.GetInt32(2)));
        return list;
    }

    /// <summary>Cria (ou reaproveita, se o nome já existe) e retorna o id.</summary>
    public long CreateCollection(string name)
    {
        using var c = Open();
        Execute(c, "INSERT OR IGNORE INTO collections (name, created_at) VALUES ($n, $now);", null,
            ("$n", name.Trim()), ("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        return Convert.ToInt64(Scalar(c, "SELECT id FROM collections WHERE name = $n;", null, ("$n", name.Trim())));
    }

    public void DeleteCollection(long id)
    {
        using var c = Open();
        Execute(c, "DELETE FROM collections WHERE id = $id;", null, ("$id", id));
    }

    public void SetInCollection(long collectionId, long gameId, bool member)
    {
        using var c = Open();
        Execute(c, member
            ? "INSERT OR IGNORE INTO collection_games (collection_id, game_id) VALUES ($c, $g);"
            : "DELETE FROM collection_games WHERE collection_id = $c AND game_id = $g;", null, ("$c", collectionId), ("$g", gameId));
    }

    /// <summary>coleção → ids dos jogos.</summary>
    public Dictionary<long, HashSet<long>> GetCollectionMembers()
    {
        using var c = Open();
        using var cmd = Command(c, "SELECT collection_id, game_id FROM collection_games;", null);
        using var r = cmd.ExecuteReader();
        var map = new Dictionary<long, HashSet<long>>();
        while (r.Read())
        {
            if (!map.TryGetValue(r.GetInt64(0), out var set)) map[r.GetInt64(0)] = set = [];
            set.Add(r.GetInt64(1));
        }
        return map;
    }

    // ── Estatísticas ────────────────────────────────────────────────────────

    /// <summary>Sessões de jogo desde <paramref name="since"/>: (início, segundos, sistema, título).</summary>
    public List<PlaySession> GetSessions(DateTimeOffset since)
    {
        using var c = Open();
        using var cmd = Command(c, """
            SELECT s.started_at, s.seconds, g.system_id, g.title, g.id
            FROM play_sessions s JOIN games g ON g.id = s.game_id
            WHERE s.started_at >= $since ORDER BY s.started_at;
            """, null, ("$since", since.ToUnixTimeSeconds()));
        using var r = cmd.ExecuteReader();
        var list = new List<PlaySession>();
        while (r.Read())
            list.Add(new PlaySession(DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetInt64(4)));
        return list;
    }

    public void SetFavorite(long gameId, bool favorite)
    {
        using var c = Open();
        Execute(c, "UPDATE games SET favorite = $f WHERE id = $id;", null, ("$f", favorite ? 1 : 0), ("$id", gameId));
    }

    public void SetMetadata(long gameId, string? json)
    {
        using var c = Open();
        Execute(c, "UPDATE games SET metadata_json = $j WHERE id = $id;", null, ("$j", json), ("$id", gameId));
    }

    /// <summary>Emulador escolhido para este jogo (null = o padrão do sistema).</summary>
    public void SetEmulatorOverride(long gameId, string? emulatorId)
    {
        using var c = Open();
        Execute(c, "UPDATE games SET emulator_override = $e WHERE id = $id;", null, ("$e", emulatorId), ("$id", gameId));
    }

    public void SetCover(long gameId, string? coverPath)
    {
        using var c = Open();
        Execute(c, "UPDATE games SET cover_path = $p WHERE id = $id;", null, ("$p", coverPath), ("$id", gameId));
    }

    /// <summary>Tira da biblioteca os arquivos indicados (os arquivos em disco não são tocados).</summary>
    public int RemoveGamesByPath(IEnumerable<string> paths)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var removed = paths.Sum(p => Execute(c, "DELETE FROM games WHERE path = $p;", tx, ("$p", p)));
        tx.Commit();
        return removed;
    }

    public void RemoveGame(long gameId)
    {
        using var c = Open();
        Execute(c, "DELETE FROM games WHERE id = $id;", null, ("$id", gameId));
    }

    public void RecordSession(long gameId, DateTimeOffset startedAt, long seconds)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Execute(c, "INSERT INTO play_sessions (game_id, started_at, seconds) VALUES ($id, $start, $s);", tx,
            ("$id", gameId), ("$start", startedAt.ToUnixTimeSeconds()), ("$s", seconds));
        Execute(c, "UPDATE games SET play_seconds = play_seconds + $s, last_played = $end WHERE id = $id;", tx,
            ("$s", seconds), ("$end", startedAt.ToUnixTimeSeconds() + seconds), ("$id", gameId));
        tx.Commit();
    }

    // ── Pastas de ROMs ──────────────────────────────────────────────────────

    public List<string> GetRomFolders()
    {
        using var c = Open();
        using var cmd = Command(c, "SELECT path FROM rom_folders ORDER BY path COLLATE NOCASE;", null);
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read())
            list.Add(r.GetString(0));
        return list;
    }

    public void AddRomFolder(string path)
    {
        using var c = Open();
        Execute(c, "INSERT OR IGNORE INTO rom_folders (path, added_at) VALUES ($p, $now);", null,
            ("$p", Path.GetFullPath(path)), ("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    public void RemoveRomFolder(string path)
    {
        using var c = Open();
        Execute(c, "DELETE FROM rom_folders WHERE path = $p;", null, ("$p", path));
    }

    // ── Emuladores instalados ───────────────────────────────────────────────

    public List<InstalledEmulator> GetInstalledEmulators()
    {
        using var c = Open();
        using var cmd = Command(c, "SELECT id, version, install_dir, executable_path, sha256, verified, installed_at FROM installed_emulators;", null);
        using var r = cmd.ExecuteReader();
        var list = new List<InstalledEmulator>();
        while (r.Read())
        {
            list.Add(new InstalledEmulator
            {
                Id = r.GetString(0),
                Version = r.GetString(1),
                InstallDir = r.GetString(2),
                ExecutablePath = r.GetString(3),
                Sha256 = r.IsDBNull(4) ? null : r.GetString(4),
                Verified = r.GetInt64(5) != 0,
                InstalledAt = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(6)),
            });
        }
        return list;
    }

    public void SaveInstalledEmulator(InstalledEmulator e)
    {
        using var c = Open();
        Execute(c, """
            INSERT OR REPLACE INTO installed_emulators (id, version, install_dir, executable_path, sha256, verified, installed_at)
            VALUES ($id, $v, $dir, $exe, $sha, $ok, $at);
            """, null,
            ("$id", e.Id), ("$v", e.Version), ("$dir", e.InstallDir), ("$exe", e.ExecutablePath),
            ("$sha", e.Sha256), ("$ok", e.Verified ? 1 : 0), ("$at", e.InstalledAt.ToUnixTimeSeconds()));
    }

    public void RemoveInstalledEmulator(string id)
    {
        using var c = Open();
        Execute(c, "DELETE FROM installed_emulators WHERE id = $id;", null, ("$id", id));
    }

    // ── Configurações ───────────────────────────────────────────────────────

    public string? GetSetting(string key)
    {
        using var c = Open();
        return Scalar(c, "SELECT value FROM settings WHERE key = $k;", null, ("$k", key)) as string;
    }

    public void SetSetting(string key, string? value)
    {
        using var c = Open();
        Execute(c, "INSERT OR REPLACE INTO settings (key, value) VALUES ($k, $v);", null, ("$k", key), ("$v", value));
    }

    // ── Infra ───────────────────────────────────────────────────────────────

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        Execute(c, "PRAGMA foreign_keys = ON;", null);
        return c;
    }

    private static Game ReadGame(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        SystemId = r.GetString(1),
        Title = r.GetString(2),
        Path = r.GetString(3),
        Serial = r.IsDBNull(4) ? null : r.GetString(4),
        Crc32 = r.IsDBNull(5) ? null : r.GetString(5),
        SizeBytes = r.GetInt64(6),
        CoverPath = r.IsDBNull(7) ? null : r.GetString(7),
        MetadataJson = r.IsDBNull(8) ? null : r.GetString(8),
        Favorite = r.GetInt64(9) != 0,
        PlaySeconds = r.GetInt64(10),
        LastPlayed = r.IsDBNull(11) ? null : DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(11)),
        AddedAt = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(12)),
        EmulatorOverride = r.IsDBNull(13) ? null : r.GetString(13),
        Source = r.GetString(14),
    };

    private static SqliteCommand Command(SqliteConnection c, string sql, SqliteTransaction? tx, params (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static int Execute(SqliteConnection c, string sql, SqliteTransaction? tx, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(c, sql, tx, args);
        return cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection c, string sql, SqliteTransaction? tx, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(c, sql, tx, args);
        var value = cmd.ExecuteScalar();
        return value is DBNull ? null : value;
    }
}
