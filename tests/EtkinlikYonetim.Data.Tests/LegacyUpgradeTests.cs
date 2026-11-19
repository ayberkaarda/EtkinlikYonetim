using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>Runs against a real SQL Server instance; see <see cref="SqlServerTestDatabase"/>.</summary>
public sealed class LegacyUpgradeTests
{
    private const string BackupEnvironmentVariable = "ETKINLIK_LEGACY_BACKUP";

    // Schema of the original application's database (read from its backup): system-named primary,
    // unique, default and check constraints, plain text passwords in NVARCHAR(100), no foreign keys,
    // a participant's single event stored by name, no migration history.
    internal const string LegacySchema = """
        CREATE TABLE dbo.Kullanicilar (
            Id INT IDENTITY(1,1) PRIMARY KEY,
            KullaniciAdi NVARCHAR(50) NOT NULL UNIQUE,
            Sifre NVARCHAR(100) NOT NULL,
            Rol NVARCHAR(20) NULL DEFAULT 'Kullanici' CHECK (Rol = 'Kullanici' OR Rol = 'Admin'));
        CREATE TABLE dbo.Etkinlikler (
            Id INT IDENTITY(1,1) PRIMARY KEY,
            EtkinlikAdi NVARCHAR(100) NOT NULL,
            Tarih DATE NOT NULL,
            Kapasite INT NOT NULL);
        CREATE TABLE dbo.Katilimcilar (
            Id INT IDENTITY(1,1) PRIMARY KEY,
            EtkinlikAdi NVARCHAR(100) NULL,
            TcKimlikNo NVARCHAR(11) NOT NULL,
            AdSoyad NVARCHAR(100) NULL,
            Email NVARCHAR(100) NULL);
        """;

    // Fictional rows. Identity values deliberately do not start at 1 so overwritten or re-seeded rows
    // would show. "admin" in lower case and a missing role are what the original application allowed.
    // The open events are dated by the database's clock, which decides what is past: today and in 30 days.
    internal const string LegacyData = """
        SET IDENTITY_INSERT dbo.Kullanicilar ON;
        INSERT dbo.Kullanicilar (Id, KullaniciAdi, Sifre, Rol) VALUES
            (3, N'yonetici', N'admin1', N'admin'),
            (5, N'uye', N'1234', N'Kullanici'),
            (6, N'rolsuz', N'abcd', NULL);
        SET IDENTITY_INSERT dbo.Kullanicilar OFF;
        SET IDENTITY_INSERT dbo.Etkinlikler ON;
        INSERT dbo.Etkinlikler (Id, EtkinlikAdi, Tarih, Kapasite) VALUES
            (2, N'Eski Konser', CAST(SYSUTCDATETIME() AS date), 2),
            (4, N'Geçmiş Etkinlik', '2025-05-17', 10),
            (7, N'Boş Etkinlik', DATEADD(DAY, 30, CAST(SYSUTCDATETIME() AS date)), 5);
        SET IDENTITY_INSERT dbo.Etkinlikler OFF;
        SET IDENTITY_INSERT dbo.Katilimcilar ON;
        INSERT dbo.Katilimcilar (Id, EtkinlikAdi, TcKimlikNo, AdSoyad, Email) VALUES
            (3, N'Eski Konser', N'20000000001', N'Eski Katılımcı', N'eski@example.com'),
            (5, N'Geçmiş Etkinlik', N'20000000002', N'Geçmiş Katılımcı', NULL),
            (8, NULL, N'20000000003', N'Kayıtsız Katılımcı', NULL);
        SET IDENTITY_INSERT dbo.Katilimcilar OFF;
        """;

    [Fact]
    public async Task Initialize_LegacyDatabase_PreservesDataAndTurnsEventNamesIntoRegistrations()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.CreateAsync(ct);
        await database.ExecuteAsync(LegacySchema, ct);
        await database.ExecuteAsync(LegacyData, ct);

        await using var provider = database.BuildServices();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync(ct);
        await initializer.InitializeAsync(ct); // Second start must be a no-op.

        // Existing rows and identities are kept; no sample data was added.
        Assert.Equal(3, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Id IN (3, 5, 6)", ct));
        Assert.Equal(3, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", ct));
        Assert.Equal(3, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Etkinlikler", ct));
        Assert.Equal(3, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Katilimcilar WHERE KullaniciId IS NULL", ct));
        Assert.Equal(3, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE SifreBicimi = 1 AND BasarisizGiris = 0 AND KilitBitis IS NULL", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Id = 3 AND Rol COLLATE Latin1_General_BIN = N'Admin'", ct));
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Id IN (5, 6) AND Rol COLLATE Latin1_General_BIN = N'Kullanici'", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%_InitialCreate'", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%_ScopeNationalIdToOwner'", ct));

        // Each event assignment became one active registration with a ticket; the counters follow.
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Katilimcilar') AND name = N'EtkinlikAdi'", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE KatilimciId = 3 AND EtkinlikId = 2 AND Durum = 0", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE KatilimciId = 5 AND EtkinlikId = 4 AND Durum = 0", ct));
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar", ct));
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Biletler WHERE BiletKodu LIKE N'ETK-%'", ct));

        // The short codes of the upgrade were reissued with 128 random bits, each with the hash the entrance looks up.
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(DISTINCT BiletKodu) FROM dbo.Biletler WHERE LEN(BiletKodu) = 36 AND BiletKoduOzeti = HASHBYTES('SHA2_256', CONVERT(varchar(40), BiletKodu))", ct));
        var legacyCode = await database.TextAsync("SELECT BiletKodu FROM dbo.Biletler b JOIN dbo.Kayitlar r ON r.Id = b.KayitId WHERE r.EtkinlikId = 2", ct);
        Assert.NotNull(legacyCode);
        Assert.Equal(TicketCodeHash.Of(legacyCode), await database.BytesAsync("SELECT BiletKoduOzeti FROM dbo.Biletler WHERE BiletKodu = @code", ct, ("@code", legacyCode)));
        var checkIn = await provider.GetRequiredService<ICheckInService>().CheckInAsync(TestServices.Admin(3), 2, legacyCode.ToLowerInvariant(), ct);
        Assert.Equal(CheckInOutcome.CheckedIn, checkIn.Outcome);
        await database.ExecuteAsync("UPDATE dbo.Biletler SET GirisZamani = NULL", ct);
        Assert.Equal(1, await database.ScalarAsync("SELECT KayitSayisi FROM dbo.Etkinlikler WHERE Id = 2", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT KayitSayisi FROM dbo.Etkinlikler WHERE Id = 4", ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT KayitSayisi FROM dbo.Etkinlikler WHERE Id = 7", ct));
        await AssertUpgradedSchemaAsync(database, ct);

        // Plain text passwords still work once and are replaced with hashes; roles are kept.
        var auth = provider.GetRequiredService<IAuthService>();
        var admin = await auth.SignInAsync("yonetici", "admin1", ct);
        var user = await auth.SignInAsync("uye", "1234", ct);
        Assert.True(admin.IsAdmin);
        Assert.Equal(UserRole.User, user.Role);
        Assert.Equal(UserRole.User, (await auth.SignInAsync("rolsuz", "abcd", ct)).Role);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Id = 3 AND Sifre LIKE N'PBKDF2-SHA256$%' AND SifreBicimi = 0", ct));
        await auth.SignInAsync("yonetici", "admin1", ct);

        // The upgraded registrations are visible to admins only; participants of the original
        // application belong to no account.
        var registrations = provider.GetRequiredService<IRegistrationService>();
        Assert.Equal(2, (await registrations.GetMyRegistrationsAsync(admin, ct)).Count);
        Assert.Empty(await registrations.GetMyRegistrationsAsync(user, ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => registrations.RegisterAsync(user, 7, "20000000003", ct));

        // New rows work on the upgraded schema, continue after the existing identities and respect
        // the derived seat counters.
        var receipt = await registrations.RegisterAsync(admin, 2, "20000000003", ct);
        Assert.True(receipt.RegistrationId > 2);
        var full = await registrations.RegisterAsync(admin, 2, "20000000002", ct);
        Assert.Equal((null, 1), (full.TicketCode, full.QueuePosition)); // the derived counter says the event is full
        var participantId = await provider.GetRequiredService<IParticipantService>().AddAsync(user, "10000000450", "Yeni Katılımcı", "yeni@example.com", ct);
        Assert.True(participantId > 8);
        await registrations.RegisterAsync(user, 7, "10000000450", ct);
        await auth.RegisterAsync("yeniuye", "Gizli.Sifre1", "Gizli.Sifre1", ct);
        Assert.True(await database.ScalarAsync("SELECT Id FROM dbo.Kullanicilar WHERE KullaniciAdi = N'yeniuye'", ct) > 6);

        // The database itself refuses rows that break the new constraints.
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
            "INSERT dbo.Kayitlar (KatilimciId, EtkinlikId, KayitZamani, Durum) VALUES (3, 2, SYSUTCDATETIME(), 0)",
            ct));
        Assert.Contains("IX_Kayitlar_KatilimciId_EtkinlikId", duplicate.Message, StringComparison.Ordinal);
        var role = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
            "INSERT dbo.Kullanicilar (KullaniciAdi, Sifre, SifreBicimi, Rol, BasarisizGiris, DevreDisi, OturumSurumu) VALUES (N'x', N'-', 0, N'Sahip', 0, 0, 0)",
            ct));
        Assert.Contains("CK_Kullanicilar_Rol", role.Message, StringComparison.Ordinal);
        var nationalId = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
            "INSERT dbo.Katilimcilar (TcKimlikNo) VALUES (N'20000000002')",
            ct));
        Assert.Contains("IX_Katilimcilar_KullaniciId_TcKimlikNo", nationalId.Message, StringComparison.Ordinal);

        // The same number under an account is another account's own entry, not a duplicate.
        await database.ExecuteAsync("INSERT dbo.Katilimcilar (TcKimlikNo, KullaniciId) VALUES (N'20000000002', 5)", ct);
        var sameOwner = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
            "INSERT dbo.Katilimcilar (TcKimlikNo, KullaniciId) VALUES (N'20000000002', 5)",
            ct));
        Assert.Contains("IX_Katilimcilar_KullaniciId_TcKimlikNo", sameOwner.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The original application let users pick any text, including text that starts like a stored
    /// hash. The stored format column, not the value, decides how a password is checked.
    /// </summary>
    [Fact]
    public async Task Initialize_LegacyPasswordWithHashPrefix_CanSignInAndUpgrade()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Password = "PBKDF2-SHA256$1000$a$b";
        await using var database = await SqlServerTestDatabase.CreateAsync(ct);
        await database.ExecuteAsync(LegacySchema, ct);
        await database.ExecuteAsync("INSERT dbo.Kullanicilar (KullaniciAdi, Sifre, Rol) VALUES (N'onekli', @password, N'Kullanici')", ct, ("@password", Password));

        await using var provider = database.BuildServices();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        var auth = provider.GetRequiredService<IAuthService>();

        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE SifreBicimi = 1", ct));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await auth.SignInAsync("onekli", Password, ct);
        }

        // The first sign-in replaced the value and its format together; knowing the stored hash is
        // not knowing the password, there is no plain text fallback.
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE SifreBicimi = 0 AND Sifre LIKE N'PBKDF2-SHA256$%' AND Sifre <> @password",
            ct,
            ("@password", Password)));
        var hash = await database.TextAsync("SELECT Sifre FROM dbo.Kullanicilar WHERE KullaniciAdi = N'onekli'", ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("onekli", hash, ct));
    }

    [Fact]
    public async Task Initialize_LegacyDatabaseWithConflictingRows_RefusesWithoutChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.CreateAsync(ct);
        await database.ExecuteAsync(LegacySchema, ct);
        await database.ExecuteAsync(
            """
            INSERT dbo.Etkinlikler (EtkinlikAdi, Tarih, Kapasite) VALUES (N'Dolu Etkinlik', '2026-03-10', 1);
            INSERT dbo.Katilimcilar (EtkinlikAdi, TcKimlikNo, AdSoyad) VALUES
                (N'Dolu Etkinlik', N'20000000001', N'Gizli Ad'),
                (N'Dolu Etkinlik', N'20000000002', N'Gizli Ad'),
                (N'Silinmiş Etkinlik', N'20000000003', N'Gizli Ad'),
                (NULL, N'20000000003', N'Gizli Ad');
            """,
            ct);

        await using var provider = database.BuildServices();
        var error = await Assert.ThrowsAsync<DatabaseInitializationException>(
            () => provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct));

        var message = error.InnerException?.Message;
        Assert.Contains("1 participants whose event name matches no event", message, StringComparison.Ordinal);
        Assert.Contains("1 events with more participants than their capacity", message, StringComparison.Ordinal);
        Assert.Contains("1 national ids shared by more than one participant of the same owner", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Gizli", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Dolu", message, StringComparison.Ordinal);
        Assert.DoesNotContain("2000000000", message, StringComparison.Ordinal);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN (N'__EFMigrationsHistory', N'Kayitlar', N'Biletler')", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Katilimcilar') AND name = N'EtkinlikAdi'", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM sys.default_constraints", ct));
    }

    /// <summary>
    /// The original sign-in compared <c>rol.ToLower() == "admin"</c>, so a role with a trailing space
    /// opened the user panel, while SQL Server comparisons ignore trailing spaces and would match
    /// "Admin". Such roles, whose meaning the upgrade cannot decide, stop the upgrade before anything
    /// is changed instead of being turned into either role.
    /// </summary>
    [Theory]
    [InlineData("admin ")]
    [InlineData("Admin  ")]
    [InlineData("Kullanici ")]
    public async Task Initialize_PaddedLegacyRole_DoesNotGrantAdmin(string role)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.CreateAsync(ct);
        await database.ExecuteAsync(LegacySchema, ct);
        await database.ExecuteAsync(
            "INSERT dbo.Kullanicilar (KullaniciAdi, Sifre, Rol) VALUES (N'bosluklu', N'gizli1', @role), (N'yonetici', N'admin1', N'Admin')",
            ct,
            ("@role", role));

        await using var provider = database.BuildServices();
        var error = await Assert.ThrowsAsync<DatabaseInitializationException>(
            () => provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct));

        var message = error.InnerException?.Message;
        Assert.Contains("1 users with a missing or too long name or password or an unknown role", message, StringComparison.Ordinal);
        Assert.Contains("No changes were made.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("bosluklu", message, StringComparison.Ordinal);
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'bosluklu' AND CONVERT(varbinary(100), Rol) = CONVERT(varbinary(100), @role)",
            ct,
            ("@role", role)));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN (N'__EFMigrationsHistory', N'Kayitlar', N'Biletler')", ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Kullanicilar') AND name = N'SifreBicimi'", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Katilimcilar') AND name = N'EtkinlikAdi'", ct));
        await Assert.ThrowsAsync<DatabaseInitializationException>(() => provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct));
    }

    /// <summary>Exactly the role values the original application wrote are still upgraded, each to its own role.</summary>
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("admin", true)]
    [InlineData("Kullanici", false)]
    [InlineData("kullanici", false)]
    [InlineData(null, false)]
    public async Task Initialize_ExactLegacyRole_KeepsItsMeaning(string? role, bool admin)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.CreateAsync(ct);
        await database.ExecuteAsync(LegacySchema, ct);
        await database.ExecuteAsync(
            "INSERT dbo.Kullanicilar (KullaniciAdi, Sifre, Rol) VALUES (N'hesap', N'gizli1', @role)",
            ct,
            ("@role", (object?)role ?? DBNull.Value));

        await using var provider = database.BuildServices();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);

        var expected = admin ? "Admin" : "Kullanici";
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE CONVERT(varbinary(100), Rol) = CONVERT(varbinary(100), @role)",
            ct,
            ("@role", expected)));
        Assert.Equal(admin, (await provider.GetRequiredService<IAuthService>().SignInAsync("hesap", "gizli1", ct)).IsAdmin);
        Assert.False(await provider.GetRequiredService<ISetupService>().IsSetupRequiredAsync(ct));
        Assert.Equal(!admin, await provider.GetRequiredService<ISetupService>().IsAdminMissingAsync(ct));
    }

    /// <summary>
    /// An upgraded database whose accounts are all users keeps its sign-in: whoever opens the
    /// application cannot make themselves admin, only the holder of a key issued for this database can, once.
    /// </summary>
    [Fact]
    public async Task Initialize_LegacyDatabaseWithoutAdmin_NeedsTheSetupKeyForTheFirstAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.CreateAsync(ct);
        await database.ExecuteAsync(LegacySchema, ct);
        await database.ExecuteAsync("INSERT dbo.Kullanicilar (KullaniciAdi, Sifre, Rol) VALUES (N'eski', N'gizli1', N'Kullanici')", ct);

        await using var provider = database.BuildServices();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        var setup = provider.GetRequiredService<ISetupService>();
        const string Admins = "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'";

        Assert.False(await setup.IsSetupRequiredAsync(ct));
        Assert.True(await setup.IsAdminMissingAsync(ct));
        var key = await database.IssueSetupKeyAsync(ct);
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", ct));
        Assert.Equal(SetupService.SetupKeyRequired, refused.Message);
        Assert.Equal(0, await database.ScalarAsync(Admins, ct));

        Assert.True((await setup.CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", ct)).IsAdmin);
        Assert.Equal(1, await database.ScalarAsync(Admins, ct));
        Assert.False((await provider.GetRequiredService<IAuthService>().SignInAsync("eski", "gizli1", ct)).IsAdmin);
    }

    /// <summary>
    /// A default installation contains no account at all, so no documented or shared password can
    /// open it; the first admin is created by the first-run setup with a password of its own.
    /// </summary>
    [Fact]
    public async Task Initialize_DefaultConfiguration_DoesNotCreateDemoCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);

        await using var provider = database.BuildServices();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);

        var auth = provider.GetRequiredService<IAuthService>();
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("admin", "Demo.Admin1", ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("ornek", "Demo.Kullanici1", ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Etkinlikler", ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Katilimcilar", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%_InitialCreate'", ct));
        await AssertUpgradedSchemaAsync(database, ct);
    }

    [Fact]
    public async Task Initialize_DemoRequested_AddsFictionalSampleDataOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);

        await using var provider = database.BuildServices(demo: true);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);

        var session = await provider.GetRequiredService<IAuthService>().SignInAsync("admin", "Demo.Admin1", ct);
        Assert.True(session.IsAdmin);
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE SifreBicimi = 0", ct));
        Assert.Equal(3, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Etkinlikler WHERE KayitSayisi = 0", ct));
        Assert.Equal(3, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Katilimcilar", ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar", ct));
        Assert.False(await provider.GetRequiredService<ISetupService>().IsSetupRequiredAsync(ct));
        await AssertUpgradedSchemaAsync(database, ct);
    }

    /// <summary>The demo switch never adds its accounts to a database that already has data.</summary>
    [Fact]
    public async Task Initialize_DemoRequested_LeavesADatabaseWithDataAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using (var plain = database.BuildServices())
        {
            await plain.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
            await plain.GetRequiredService<ISetupService>().CreateFirstAdminAsync(null, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", ct);
        }

        await using var provider = database.BuildServices(demo: true);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);

        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Etkinlikler", ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => provider.GetRequiredService<IAuthService>().SignInAsync("admin", "Demo.Admin1", ct));
    }

    /// <summary>
    /// Restores a backup of the original application's database (path in ETKINLIK_LEGACY_BACKUP,
    /// readable by the SQL Server service) and upgrades it. Only counts, identities and checksums are
    /// compared, so no personal data from the backup ends up in test output.
    /// </summary>
    [Fact]
    public async Task Initialize_RestoredLegacyBackup_KeepsAllRows()
    {
        var backup = Environment.GetEnvironmentVariable(BackupEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(backup))
        {
            Assert.Skip($"{BackupEnvironmentVariable} is not set.");
        }

        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await RestoreAsync(database, backup, ct);

        const string Counts = """
            SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM (
                SELECT 'U' AS T, COUNT(*) AS C, MAX(Id) AS M FROM dbo.Kullanicilar
                UNION ALL SELECT 'E', COUNT(*), MAX(Id) FROM dbo.Etkinlikler
                UNION ALL SELECT 'P', COUNT(*), MAX(Id) FROM dbo.Katilimcilar) s
            """;
        const string Users = "SELECT CHECKSUM_AGG(CHECKSUM(Id, KullaniciAdi, Sifre)) FROM dbo.Kullanicilar";
        const string Events = "SELECT CHECKSUM_AGG(CHECKSUM(Id, EtkinlikAdi, Tarih, Kapasite)) FROM dbo.Etkinlikler";
        const string People = "SELECT CHECKSUM_AGG(CHECKSUM(Id, TcKimlikNo, AdSoyad, Email)) FROM dbo.Katilimcilar";
        const string Admins = "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol COLLATE Latin1_General_CI_AS = N'Admin'";
        var countsBefore = await database.ScalarAsync(Counts, ct);
        var usersBefore = await database.ScalarAsync(Users, ct);
        var eventsBefore = await database.ScalarAsync(Events, ct);
        var peopleBefore = await database.ScalarAsync(People, ct);
        var adminsBefore = await database.ScalarAsync(Admins, ct);
        var assignmentsBefore = await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Katilimcilar WHERE EtkinlikAdi IS NOT NULL", ct);
        var assignmentChecksum = await database.ScalarAsync(
            "SELECT CHECKSUM_AGG(CHECKSUM(k.Id, e.Id)) FROM dbo.Katilimcilar k JOIN dbo.Etkinlikler e ON e.EtkinlikAdi = k.EtkinlikAdi",
            ct);

        await using var provider = database.BuildServices();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);

        Assert.True(assignmentsBefore > 0, "The backup has no event assignments.");
        Assert.Equal(countsBefore, await database.ScalarAsync(Counts, ct));
        Assert.Equal(usersBefore, await database.ScalarAsync(Users, ct));
        Assert.Equal(eventsBefore, await database.ScalarAsync(Events, ct));
        Assert.Equal(peopleBefore, await database.ScalarAsync(People, ct));
        Assert.Equal(adminsBefore, await database.ScalarAsync(Admins, ct));
        Assert.Equal(assignmentsBefore, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE Durum = 0", ct));
        Assert.Equal(assignmentsBefore, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Biletler", ct));
        Assert.Equal(assignmentsBefore, await database.ScalarAsync("SELECT SUM(KayitSayisi) FROM dbo.Etkinlikler", ct));
        Assert.Equal(assignmentChecksum, await database.ScalarAsync("SELECT CHECKSUM_AGG(CHECKSUM(KatilimciId, EtkinlikId)) FROM dbo.Kayitlar", ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE SifreBicimi <> 1", ct));
        await AssertUpgradedSchemaAsync(database, ct);
    }

    private static async Task AssertUpgradedSchemaAsync(SqlServerTestDatabase database, CancellationToken ct)
    {
        string[] expected =
        [
            "PK_Kullanicilar", "PK_Etkinlikler", "PK_Katilimcilar", "PK_Kayitlar", "PK_Biletler",
            "FK_Katilimcilar_Kullanicilar", "FK_Kayitlar_Katilimcilar", "FK_Kayitlar_Etkinlikler", "FK_Biletler_Kayitlar",
            "CK_Kullanicilar_Rol", "CK_Kullanicilar_BasarisizGiris", "CK_Kullanicilar_SifreBicimi",
            "CK_Etkinlikler_Kapasite", "CK_Etkinlikler_KayitSayisi", "CK_Kayitlar_Durum",
            "PK_KurulumAnahtarlari",
        ];
        foreach (var name in expected)
        {
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM sys.objects WHERE name = @name", ct, ("@name", name)));
        }

        // The system-named unique, default and check constraints are replaced by named ones.
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.key_constraints WHERE type = 'UQ'", ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.default_constraints", ct));
        Assert.Equal(expected.Length, await database.ScalarAsync("SELECT COUNT(*) FROM sys.objects WHERE type IN ('PK', 'F', 'C') AND parent_object_id <> OBJECT_ID(N'dbo.__EFMigrationsHistory')", ct));
        Assert.Equal(4, await database.ScalarAsync(
            "SELECT COUNT(*) FROM sys.indexes WHERE is_unique = 1 AND has_filter = 0 AND name IN (N'IX_Kullanicilar_KullaniciAdi', N'IX_Katilimcilar_KullaniciId_TcKimlikNo', N'IX_Biletler_KayitId', N'IX_Biletler_BiletKodu')",
            ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_Katilimcilar_TcKimlikNo'", ct));
        Assert.Equal(2, await database.ScalarAsync(
            "SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id WHERE i.name = N'IX_Katilimcilar_KullaniciId_TcKimlikNo'",
            ct));
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_Kayitlar_KatilimciId_EtkinlikId' AND is_unique = 1 AND has_filter = 1 AND filter_definition = N'([Durum] IN ((0), (2)))'",
            ct));
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_Kayitlar_EtkinlikId_SiraNo' AND is_unique = 1 AND has_filter = 1 AND filter_definition = N'([SiraNo] IS NOT NULL)'",
            ct));
        Assert.Equal(3, await database.ScalarAsync(
            "SELECT COUNT(*) FROM sys.indexes WHERE is_unique = 0 AND name IN (N'IX_Etkinlikler_Tarih', N'IX_Katilimcilar_KullaniciId', N'IX_Kayitlar_EtkinlikId')",
            ct));
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Kullanicilar') AND name = N'Sifre' AND max_length = 512",
            ct));
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(N'dbo.Etkinlikler') AND c.name = N'Tarih' AND t.name = N'date'",
            ct));
    }

    private static async Task RestoreAsync(SqlServerTestDatabase database, string backup, CancellationToken ct)
    {
        await SqlServerTestDatabase.ExecuteAsync(
            database.Server,
            """
            DECLARE @files TABLE (
                LogicalName nvarchar(128), PhysicalName nvarchar(260), Type char(1), FileGroupName nvarchar(128), Size numeric(20,0),
                MaxSize numeric(20,0), FileId bigint, CreateLSN numeric(25,0), DropLSN numeric(25,0), UniqueId uniqueidentifier,
                ReadOnlyLSN numeric(25,0), ReadWriteLSN numeric(25,0), BackupSizeInBytes bigint, SourceBlockSize int, FileGroupId int,
                LogGroupGUID uniqueidentifier, DifferentialBaseLSN numeric(25,0), DifferentialBaseGUID uniqueidentifier, IsReadOnly bit,
                IsPresent bit, TDEThumbprint varbinary(32), SnapshotUrl nvarchar(360));
            INSERT @files EXEC sp_executesql N'RESTORE FILELISTONLY FROM DISK = @p', N'@p nvarchar(4000)', @p = @backup;
            DECLARE @data nvarchar(128) = (SELECT LogicalName FROM @files WHERE Type = 'D');
            DECLARE @log nvarchar(128) = (SELECT LogicalName FROM @files WHERE Type = 'L');
            DECLARE @path nvarchar(400) = CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400));
            DECLARE @mdf nvarchar(500) = @path + @name + N'.mdf';
            DECLARE @ldf nvarchar(500) = @path + @name + N'_log.ldf';
            RESTORE DATABASE @name FROM DISK = @backup WITH MOVE @data TO @mdf, MOVE @log TO @ldf, RECOVERY;
            """,
            ct,
            ("@name", database.Name),
            ("@backup", backup));
    }
}
