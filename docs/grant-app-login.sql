-- Rights of the application's own login on the EtkinlikYonetim database (example).
--
-- Run it as the database owner, connected to the application's database, after the first start with
-- the owner's connection has created or upgraded the schema. Afterwards point the application's
-- connection string at this login. It may read and write the account and event tables, but on the
-- setup key table only read and mark a key used: it cannot create, replace, extend or delete a setup
-- key. Keys come only from docs/issue-setup-key.sql, run by the database owner.
--
-- Create the login and the user first, for example:
--   CREATE LOGIN [DOMAIN\EtkinlikYonetimApp] FROM WINDOWS;        -- or a SQL login with a password
--   CREATE USER [EtkinlikYonetimApp] FOR LOGIN [DOMAIN\EtkinlikYonetimApp];
-- then set @AppUser below to the user's name.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AppUser sysname = N'EtkinlikYonetimApp';
DECLARE @user nvarchar(300) = QUOTENAME(@AppUser);
DECLARE @sql nvarchar(max) = N'';

-- Accounts, events, participants, registrations and tickets: the application never deletes rows
-- (a cancelled registration keeps its row with a new status).
SELECT @sql += N'GRANT SELECT, INSERT, UPDATE ON dbo.' + QUOTENAME(t.name) + N' TO ' + @user + N';' + NCHAR(10)
FROM (VALUES (N'Kullanicilar'), (N'Etkinlikler'), (N'Katilimcilar'), (N'Kayitlar'), (N'Biletler')) AS t(name);

-- Setup keys: read, and mark used. No INSERT, no DELETE, no UPDATE of the hash or the expiry.
SET @sql += N'GRANT SELECT ON dbo.KurulumAnahtarlari TO ' + @user + N';' + NCHAR(10)
    + N'GRANT UPDATE (Tuketildi, TuketimTarihi) ON dbo.KurulumAnahtarlari TO ' + @user + N';' + NCHAR(10)
    + N'DENY INSERT, DELETE ON dbo.KurulumAnahtarlari TO ' + @user + N';' + NCHAR(10)
    + N'DENY UPDATE (AnahtarID, AnahtarOzeti, Olusturma, SonKullanma) ON dbo.KurulumAnahtarlari TO ' + @user + N';' + NCHAR(10)
    -- The start-up check of the migrations only reads their history.
    + N'GRANT SELECT ON dbo.__EFMigrationsHistory TO ' + @user + N';' + NCHAR(10);

EXEC sys.sp_executesql @sql;
