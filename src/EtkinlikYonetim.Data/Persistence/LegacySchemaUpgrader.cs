using System.Data;
using System.Globalization;
using System.Reflection;
using EtkinlikYonetim.Data.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>
/// Adopts a database created by the original application (Kullanicilar, Etkinlikler and Katilimcilar
/// without an EF Core migrations history). The existing rows, identity values and plain text passwords
/// are kept; passwords are marked as legacy plain text and hashed on each account's next successful
/// sign-in. The original application stored a participant's single event as its name in
/// Katilimcilar.EtkinlikAdi; each such assignment becomes an active registration with a ticket in
/// Kayitlar and Biletler, the events' seat counters are filled in and the name column is removed. The
/// remaining columns, constraints and indexes of <see cref="InitialCreate"/> are added and the
/// migration is recorded as applied.
/// </summary>
/// <remarks>
/// Every step checks the current schema first, and the whole upgrade runs in one transaction under
/// an application lock, so it is safe to run repeatedly and from concurrent processes.
/// </remarks>
internal static class LegacySchemaUpgrader
{
    private static readonly string[] LegacyTables = ["Kullanicilar", "Etkinlikler", "Katilimcilar"];

    /// <summary>Identifier of the first migration; a database without it in its history may be legacy.</summary>
    internal static string BaselineMigrationId { get; } = MigrationId<InitialCreate>();

    /// <summary>The migrations whose schema the upgrade creates itself and records as applied.</summary>
    internal static IReadOnlyList<string> AdoptedMigrationIds { get; } = [BaselineMigrationId];

    private static string MigrationId<TMigration>()
        where TMigration : Migration =>
        typeof(TMigration).GetCustomAttribute<MigrationAttribute>()!.Id;

    /// <summary>
    /// Upgrades a legacy database in place. Returns <see langword="false"/> when there is nothing to
    /// adopt (no database yet, an empty database, or the baseline migration is already applied).
    /// </summary>
    public static async Task<bool> UpgradeIfLegacyAsync(EventDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (!await IsLegacyAsync(db, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            db,
            static async (context, token) =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);

                await AcquireUpgradeLockAsync(context, token).ConfigureAwait(false);

                // Another process may have finished the upgrade while this one waited for the lock.
                if (!await IsLegacyAsync(context, token).ConfigureAwait(false))
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                    return false;
                }

                await EnsureLegacyShapeAsync(context, token).ConfigureAwait(false);
                await ValidateDataAsync(context, token).ConfigureAwait(false);

                foreach (var statement in UpgradeStatements)
                {
                    await ExecuteAsync(context, statement, token).ConfigureAwait(false);
                }

                await MarkAdoptedMigrationsAppliedAsync(context, token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return true;
            },
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes the transaction scoped application lock that serializes upgrades. sp_getapplock reports
    /// failures through its return code instead of raising an error, so the code is checked and the
    /// upgrade stops before any schema statement runs when the lock was not granted.
    /// </summary>
    private static async Task AcquireUpgradeLockAsync(EventDbContext db, CancellationToken cancellationToken)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
#pragma warning disable EF1002 // Constant statement; the only parameter is the output value.
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = N'EtkinlikYonetim.LegacySchemaUpgrade', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 60000;",
            [result],
            cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002

        EnsureLockGranted(result.Value is int code ? code : null);
    }

    /// <summary>Throws when an sp_getapplock return code means the lock was not granted.</summary>
    internal static void EnsureLockGranted(int? returnCode)
    {
        if (returnCode is >= 0)
        {
            return;
        }

        var reason = returnCode switch
        {
            -1 => "the request timed out because another upgrade is still running",
            -2 => "the request was canceled",
            -3 => "the request was chosen as a deadlock victim",
            -999 => "the lock request was invalid",
            null => "the lock procedure returned no result",
            _ => "the lock procedure failed",
        };

        throw new InvalidOperationException(
            $"The legacy database upgrade lock could not be acquired ({reason}, sp_getapplock returned "
            + $"{(returnCode?.ToString(CultureInfo.InvariantCulture) ?? "null")}). No changes were made; try again later.");
    }

    private static async Task<bool> IsLegacyAsync(EventDbContext db, CancellationToken cancellationToken)
    {
        var applied = await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        if (applied.Contains(BaselineMigrationId, StringComparer.Ordinal))
        {
            return false;
        }

        return await CountExistingLegacyTablesAsync(db, cancellationToken).ConfigureAwait(false) > 0;
    }

    private static Task<int> CountExistingLegacyTablesAsync(EventDbContext db, CancellationToken cancellationToken) =>
        ScalarAsync(
            db,
            "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN (N'Kullanicilar', N'Etkinlikler', N'Katilimcilar')",
            cancellationToken);

    /// <summary>All three tables, and the event name column of the participants, must be there.</summary>
    private static async Task EnsureLegacyShapeAsync(EventDbContext db, CancellationToken cancellationToken)
    {
        var count = await CountExistingLegacyTablesAsync(db, cancellationToken).ConfigureAwait(false);
        var eventNameColumn = await ScalarAsync(
            db,
            "SELECT CASE WHEN COL_LENGTH(N'dbo.Katilimcilar', N'EtkinlikAdi') IS NULL THEN 0 ELSE 1 END AS [Value]",
            cancellationToken).ConfigureAwait(false);
        if (count != LegacyTables.Length || eventNameColumn != 1)
        {
            throw new InvalidOperationException(
                "The database contains only part of the event schema (Kullanicilar, Etkinlikler, Katilimcilar with "
                + "Katilimcilar.EtkinlikAdi) and no migration history, so it cannot be upgraded automatically.");
        }
    }

    /// <summary>
    /// Refuses the upgrade, before anything is changed, when existing rows cannot satisfy the new
    /// constraints. Fixing such rows needs a human decision, so no data is altered or deleted here.
    /// Only counts are reported; row contents (names, national ids, passwords) never appear in the message.
    /// </summary>
    private static async Task ValidateDataAsync(EventDbContext db, CancellationToken cancellationToken)
    {
        (string Sql, string Problem)[] checks =
        [
            ("""
             SELECT COUNT(*) AS [Value] FROM dbo.Kullanicilar
             WHERE KullaniciAdi IS NULL OR LEN(KullaniciAdi) > 50 OR Sifre IS NULL OR LEN(Sifre) > 256
                OR (Rol IS NOT NULL AND Rol COLLATE Latin1_General_CI_AS NOT IN (N'Kullanici', N'Admin'))
             """,
                "users with a missing or too long name or password or an unknown role"),
            ("""
             SELECT COUNT(*) AS [Value] FROM dbo.Etkinlikler
             WHERE EtkinlikAdi IS NULL OR LEN(EtkinlikAdi) > 100 OR Tarih IS NULL OR Kapasite IS NULL OR Kapasite <= 0
             """,
                "events with a missing or too long name, a missing date or a capacity below one"),
            ("""
             SELECT COUNT(*) AS [Value] FROM dbo.Katilimcilar
             WHERE TcKimlikNo IS NULL OR LEN(TcKimlikNo) > 11 OR LEN(AdSoyad) > 100 OR LEN(Email) > 100
             """,
                "participants with a missing or too long national id, name or e-mail"),
            ("""
             SELECT COUNT(*) AS [Value] FROM (
                 SELECT TcKimlikNo FROM dbo.Katilimcilar GROUP BY TcKimlikNo HAVING COUNT(*) > 1) d
             """,
                "national ids shared by more than one participant"),
            ("""
             SELECT COUNT(*) AS [Value] FROM dbo.Katilimcilar k
             WHERE k.EtkinlikAdi IS NOT NULL
               AND (SELECT COUNT(*) FROM dbo.Etkinlikler e WHERE e.EtkinlikAdi = k.EtkinlikAdi) <> 1
             """,
                "participants whose event name matches no event or more than one event"),
            ("""
             SELECT COUNT(*) AS [Value] FROM dbo.Etkinlikler e
             WHERE (SELECT COUNT(*) FROM dbo.Katilimcilar k WHERE k.EtkinlikAdi = e.EtkinlikAdi) > e.Kapasite
             """,
                "events with more participants than their capacity"),
        ];

        var problems = new List<string>();
        foreach (var (sql, problem) in checks)
        {
            var count = await ScalarAsync(db, sql, cancellationToken).ConfigureAwait(false);
            if (count > 0)
            {
                problems.Add($"{count} {problem}");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The existing event database cannot be upgraded until these rows are corrected: "
                + string.Join("; ", problems) + ". No changes were made.");
        }
    }

    /// <summary>
    /// Brings the legacy tables to the shape created by <see cref="InitialCreate"/>. Each statement is
    /// guarded so it only runs when the corresponding part of the schema is missing.
    /// </summary>
    private static readonly string[] UpgradeStatements =
    [
        // The system-named role default and check, and the unique user name constraint, are replaced by
        // the model's named check constraint and unique index.
        DropConstraints("Kullanicilar", "D"),
        DropConstraints("Kullanicilar", "C"),
        DropConstraints("Kullanicilar", "UQ"),
        DropConstraints("Katilimcilar", "UQ"),

        // The original form compared the role case-insensitively and the database accepted "admin";
        // the stored value is now exactly "Admin" or "Kullanici" (also for rows without a role, which
        // the original column default would have made users).
        """
        UPDATE dbo.Kullanicilar SET Rol = CASE
            WHEN Rol COLLATE Latin1_General_CI_AS = N'Admin' THEN N'Admin'
            ELSE N'Kullanici' END
        WHERE Rol IS NULL OR Rol COLLATE Latin1_General_BIN NOT IN (N'Admin', N'Kullanici');
        """,

        AlterColumn("Kullanicilar", "KullaniciAdi", "nvarchar", 100, "NVARCHAR(50) NOT NULL"),
        AlterColumn("Kullanicilar", "Sifre", "nvarchar", 512, "NVARCHAR(256) NOT NULL"),
        AlterColumn("Kullanicilar", "Rol", "nvarchar", 40, "NVARCHAR(20) NOT NULL"),
        AlterColumn("Etkinlikler", "EtkinlikAdi", "nvarchar", 200, "NVARCHAR(100) NOT NULL"),
        AlterColumn("Etkinlikler", "Tarih", "date", 3, "DATE NOT NULL"),
        AlterColumn("Etkinlikler", "Kapasite", "int", 4, "INT NOT NULL"),
        AlterColumn("Katilimcilar", "TcKimlikNo", "nvarchar", 22, "NVARCHAR(11) NOT NULL"),
        AlterColumn("Katilimcilar", "AdSoyad", "nvarchar", 200, "NVARCHAR(100) NULL", nullable: true),
        AlterColumn("Katilimcilar", "Email", "nvarchar", 200, "NVARCHAR(100) NULL", nullable: true),

        // Existing accounts get the values of a fresh account; every password of the original
        // application is plain text (format 1), whatever it looks like.
        AddColumnWithInitialValue("Kullanicilar", "SifreBicimi", "INT NOT NULL", "1"),
        AddColumnWithInitialValue("Kullanicilar", "BasarisizGiris", "INT NOT NULL", "0"),
        AddColumn("Kullanicilar", "KilitBitis", "DATETIME2 NULL"),
        AddColumnWithInitialValue("Etkinlikler", "KayitSayisi", "INT NOT NULL", "0"),

        // Participants of the original application belong to no account; only admins manage them.
        AddColumn("Katilimcilar", "KullaniciId", "INT NULL"),

        RenamePrimaryKey("Kullanicilar", "PK_Kullanicilar"),
        RenamePrimaryKey("Etkinlikler", "PK_Etkinlikler"),
        RenamePrimaryKey("Katilimcilar", "PK_Katilimcilar"),

        """
        IF OBJECT_ID(N'dbo.Kayitlar', N'U') IS NULL
            EXEC(N'CREATE TABLE dbo.Kayitlar (
                Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Kayitlar PRIMARY KEY,
                KatilimciId INT NOT NULL CONSTRAINT FK_Kayitlar_Katilimcilar REFERENCES dbo.Katilimcilar (Id),
                EtkinlikId INT NOT NULL CONSTRAINT FK_Kayitlar_Etkinlikler REFERENCES dbo.Etkinlikler (Id),
                KayitZamani DATETIME2 NOT NULL,
                Durum INT NOT NULL,
                IptalZamani DATETIME2 NULL,
                CONSTRAINT CK_Kayitlar_Durum CHECK ([Durum] IN (0, 1)))');
        """,
        """
        IF OBJECT_ID(N'dbo.Biletler', N'U') IS NULL
            EXEC(N'CREATE TABLE dbo.Biletler (
                Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Biletler PRIMARY KEY,
                KayitId INT NOT NULL CONSTRAINT FK_Biletler_Kayitlar REFERENCES dbo.Kayitlar (Id),
                BiletKodu NVARCHAR(20) NOT NULL,
                OlusturmaZamani DATETIME2 NOT NULL)');
        """,

        // Each event assignment of the original application becomes an active registration with a
        // ticket, the seat counters are derived from them and the name column goes away. The column
        // only exists until this step has run, so the step cannot be repeated.
        """
        IF COL_LENGTH(N'dbo.Katilimcilar', N'EtkinlikAdi') IS NOT NULL
        BEGIN
            EXEC(N'INSERT dbo.Kayitlar (KatilimciId, EtkinlikId, KayitZamani, Durum)
                SELECT k.Id, e.Id, SYSUTCDATETIME(), 0
                FROM dbo.Katilimcilar k JOIN dbo.Etkinlikler e ON e.EtkinlikAdi = k.EtkinlikAdi
                WHERE k.EtkinlikAdi IS NOT NULL
                ORDER BY k.Id');
            EXEC(N'INSERT dbo.Biletler (KayitId, BiletKodu, OlusturmaZamani)
                SELECT r.Id, N''ETK-'' + UPPER(LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N''-'', N''''), 10)), r.KayitZamani
                FROM dbo.Kayitlar r
                WHERE NOT EXISTS (SELECT 1 FROM dbo.Biletler b WHERE b.KayitId = r.Id)');
            EXEC(N'UPDATE e SET KayitSayisi = (SELECT COUNT(*) FROM dbo.Kayitlar r WHERE r.EtkinlikId = e.Id AND r.Durum = 0)
                FROM dbo.Etkinlikler e');
            EXEC(N'ALTER TABLE dbo.Katilimcilar DROP COLUMN EtkinlikAdi');
        END
        """,

        AddForeignKey("Katilimcilar", "FK_Katilimcilar_Kullanicilar", "KullaniciId", "Kullanicilar", "Id"),

        AddCheck("Kullanicilar", "CK_Kullanicilar_Rol", SchemaLimits.RoleCheck),
        AddCheck("Kullanicilar", "CK_Kullanicilar_BasarisizGiris", SchemaLimits.FailedLoginCheck),
        AddCheck("Kullanicilar", "CK_Kullanicilar_SifreBicimi", SchemaLimits.PasswordFormatCheck),
        AddCheck("Etkinlikler", "CK_Etkinlikler_Kapasite", SchemaLimits.CapacityCheck),
        AddCheck("Etkinlikler", "CK_Etkinlikler_KayitSayisi", SchemaLimits.RegisteredCountCheck),

        CreateIndex("Kullanicilar", "IX_Kullanicilar_KullaniciAdi", "CREATE UNIQUE INDEX IX_Kullanicilar_KullaniciAdi ON dbo.Kullanicilar (KullaniciAdi)"),
        CreateIndex("Etkinlikler", "IX_Etkinlikler_Tarih", "CREATE INDEX IX_Etkinlikler_Tarih ON dbo.Etkinlikler (Tarih)"),
        CreateIndex("Katilimcilar", "IX_Katilimcilar_TcKimlikNo", "CREATE UNIQUE INDEX IX_Katilimcilar_TcKimlikNo ON dbo.Katilimcilar (TcKimlikNo)"),
        CreateIndex("Katilimcilar", "IX_Katilimcilar_KullaniciId", "CREATE INDEX IX_Katilimcilar_KullaniciId ON dbo.Katilimcilar (KullaniciId)"),
        CreateIndex(
            "Kayitlar",
            "IX_Kayitlar_KatilimciId_EtkinlikId",
            "CREATE UNIQUE INDEX IX_Kayitlar_KatilimciId_EtkinlikId ON dbo.Kayitlar (KatilimciId, EtkinlikId) WHERE "
                + SchemaLimits.ActiveRegistrationFilter),
        CreateIndex("Kayitlar", "IX_Kayitlar_EtkinlikId", "CREATE INDEX IX_Kayitlar_EtkinlikId ON dbo.Kayitlar (EtkinlikId)"),
        CreateIndex("Biletler", "IX_Biletler_KayitId", "CREATE UNIQUE INDEX IX_Biletler_KayitId ON dbo.Biletler (KayitId)"),
        CreateIndex("Biletler", "IX_Biletler_BiletKodu", "CREATE UNIQUE INDEX IX_Biletler_BiletKodu ON dbo.Biletler (BiletKodu)"),
    ];

    private static string AlterColumn(string table, string column, string typeName, int maxLength, string definition, bool nullable = false) =>
        $"""
        IF EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.{table}') AND c.name = N'{column}'
              AND (c.is_nullable <> {(nullable ? 1 : 0)} OR t.name <> N'{typeName}' OR c.max_length <> {maxLength}))
            EXEC(N'ALTER TABLE dbo.{table} ALTER COLUMN {column} {definition}');
        """;

    private static string AddColumn(string table, string column, string definition) =>
        $"""
        IF COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL
            EXEC(N'ALTER TABLE dbo.{table} ADD {column} {definition}');
        """;

    /// <summary>
    /// Adds a NOT NULL column filled with <paramref name="value"/> for the existing rows. The default
    /// constraint only exists for the duration of the statement, as the migration creates none.
    /// </summary>
    private static string AddColumnWithInitialValue(string table, string column, string definition, string value) =>
        $"""
        IF COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL
        BEGIN
            EXEC(N'ALTER TABLE dbo.{table} ADD {column} {definition} CONSTRAINT DF_Upgrade_{table}_{column} DEFAULT {value} WITH VALUES');
            EXEC(N'ALTER TABLE dbo.{table} DROP CONSTRAINT DF_Upgrade_{table}_{column}');
        END
        """;

    private static string RenamePrimaryKey(string table, string name) =>
        $"""
        DECLARE @current sysname = (
            SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.{table}') AND type = 'PK');
        IF @current IS NOT NULL AND @current <> N'{name}'
        BEGIN
            DECLARE @qualified nvarchar(300) = N'dbo.' + QUOTENAME(@current);
            EXEC sp_rename @objname = @qualified, @newname = N'{name}', @objtype = N'OBJECT';
        END
        """;

    /// <summary>
    /// Drops the constraints of one kind (D default, C check, UQ unique) of <paramref name="table"/>
    /// that do not carry a name of the model (prefixes DF_Upgrade_, CK_ and IX_ are never dropped).
    /// </summary>
    private static string DropConstraints(string table, string type) =>
        $"""
        DECLARE @drop nvarchar(max) = N'';
        SELECT @drop += N'ALTER TABLE dbo.{table} DROP CONSTRAINT ' + QUOTENAME(o.name) + N';'
        FROM sys.objects o
        WHERE o.parent_object_id = OBJECT_ID(N'dbo.{table}') AND o.type = N'{type}'
          AND o.name NOT LIKE N'CK[_]{table}[_]%';
        IF @drop <> N'' EXEC(@drop);
        """;

    /// <summary>Adds the named, non-cascading foreign key when it is missing.</summary>
    private static string AddForeignKey(string table, string name, string column, string principal, string principalColumn) =>
        $"""
        IF OBJECT_ID(N'dbo.{name}', N'F') IS NULL
            EXEC(N'ALTER TABLE dbo.{table} WITH CHECK ADD CONSTRAINT {name} FOREIGN KEY ({column}) REFERENCES dbo.{principal} ({principalColumn})');
        """;

    private static string AddCheck(string table, string name, string condition) =>
        $"""
        IF OBJECT_ID(N'dbo.{name}', N'C') IS NULL
            EXEC(N'ALTER TABLE dbo.{table} WITH CHECK ADD CONSTRAINT {name} CHECK ({condition.Replace("'", "''", StringComparison.Ordinal)})');
        """;

    private static string CreateIndex(string table, string name, string createStatement) =>
        $"""
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.{table}') AND name = N'{name}')
            EXEC(N'{createStatement.Replace("'", "''", StringComparison.Ordinal)}');
        """;

    private static async Task MarkAdoptedMigrationsAppliedAsync(EventDbContext db, CancellationToken cancellationToken)
    {
        var history = db.GetService<IHistoryRepository>();
        await ExecuteAsync(db, history.GetCreateIfNotExistsScript(), cancellationToken).ConfigureAwait(false);
        foreach (var migrationId in AdoptedMigrationIds)
        {
            await ExecuteAsync(
                db,
                history.GetInsertScript(new HistoryRow(migrationId, ProductInfo.GetVersion())),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task<int> ScalarAsync(EventDbContext db, string sql, CancellationToken cancellationToken) =>
#pragma warning disable EF1002 // The statements are built from constants in this class, never from user input.
        db.Database.SqlQueryRaw<int>(sql).SingleAsync(cancellationToken);
#pragma warning restore EF1002

    private static Task<int> ExecuteAsync(EventDbContext db, string sql, CancellationToken cancellationToken) =>
#pragma warning disable EF1002 // The statements are built from constants in this class, never from user input.
        db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
#pragma warning restore EF1002
}
