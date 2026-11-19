using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>Account changes racing on a real SQL Server instance (see <see cref="SqlServerTestDatabase"/>).</summary>
public sealed class SqlServerAccountTests
{
    /// <summary>
    /// Two admins of two application instances demote (or disable) each other at the same moment. Both
    /// pass the session check before either writes, so only the account lock and the checks inside the
    /// writing transaction can keep an admin: exactly one change wins and one enabled admin remains.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoAdminsRemovingEachOther_KeepOneEnabledAdmin(bool disable)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await using var b = database.BuildServices();
        const string EnabledAdmins = "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin' AND DevreDisi = 0";

        for (var round = 0; round < 8; round++)
        {
            await database.ExecuteAsync("UPDATE dbo.Kullanicilar SET Rol = N'Admin', DevreDisi = 0", ct);

            using var start = new ManualResetEventSlim(false);
            Task<Exception?> Remove(ServiceProvider provider, int actor, int target) => Task.Run(
                async () =>
                {
                    start.Wait(ct);
                    try
                    {
                        var accounts = provider.GetRequiredService<IAccountService>();
                        await (disable
                            ? accounts.SetEnabledAsync(Admin(actor), target, false, ct)
                            : accounts.ChangeRoleAsync(Admin(actor), target, UserRole.User, ct));
                        return null;
                    }
#pragma warning disable CA1031 // Collected and asserted below.
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        return ex;
                    }
                },
                ct);

            var first = Remove(a, SeedAdminId, SeedUserId);
            var second = Remove(b, SeedUserId, SeedAdminId);
            start.Set();
            var results = await Task.WhenAll(first, second);

            Assert.Single(results, r => r is null);
            var loser = Assert.Single(results, r => r is not null)!;
            Assert.True(
                loser is SessionExpiredException || (loser is BusinessRuleException rule && rule.Message == AccountService.LastActiveAdmin),
                loser.GetType().Name);
            Assert.Equal(1, await database.ScalarAsync(EnabledAdmins, ct));
        }
    }
}
