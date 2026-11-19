using System.Text;
using EtkinlikYonetim.Business.Exports;
using EtkinlikYonetim.Business.Services;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>The participant list exports end to end on SQLite.</summary>
public sealed class ExportTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IParticipantExportService Exports => _db.Get<IParticipantExportService>();

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    /// <summary>Seats, check-ins and the waiting list are exported with masked national ids and without a single ticket code.</summary>
    [Fact]
    public async Task Export_ContainsTheStatesButNoNationalIdOrTicketCode()
    {
        var admin = Admin(SeedAdminId);
        var registrations = _db.Get<IRegistrationService>();
        var deniz = await registrations.RegisterAsync(admin, SmallEventId, DenizNationalId, Ct);
        var ece = await registrations.RegisterAsync(admin, SmallEventId, EceNationalId, Ct);
        await registrations.RegisterAsync(admin, SmallEventId, MertNationalId, Ct); // waits
        _db.Clock.MoveTo((await _db.Get<IEventService>().GetEventsAsync(admin, Ct)).Single(e => e.Id == SmallEventId).Date);
        Assert.Equal(CheckInOutcome.CheckedIn, (await _db.Get<ICheckInService>().CheckInAsync(admin, SmallEventId, deniz.TicketCode, Ct)).Outcome);

        var csv = Encoding.UTF8.GetString((await Exports.ExportCsvAsync(admin, SmallEventId, Ct)).Content);
        var html = Encoding.UTF8.GetString((await Exports.ExportHtmlAsync(admin, SmallEventId, Ct)).Content);

        foreach (var text in new[] { csv, html })
        {
            Assert.Contains("Giriş yapıldı", text, StringComparison.Ordinal);
            Assert.Contains("Giriş yapılmadı", text, StringComparison.Ordinal);
            Assert.Contains("Bekleme listesi, sıra: 1", text, StringComparison.Ordinal);
            Assert.Contains("100******46", text, StringComparison.Ordinal);
            Assert.DoesNotContain(DenizNationalId, text, StringComparison.Ordinal);
            Assert.DoesNotContain(deniz.TicketCode!, text, StringComparison.Ordinal);
            Assert.DoesNotContain(ece.TicketCode!, text, StringComparison.Ordinal);
            Assert.DoesNotContain("ETK-", text, StringComparison.Ordinal);
        }

        Assert.Contains("Kayıtlı: 2, giriş yapan: 1, bekleme listesi: 1", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_OfAnEventWithoutRegistrations_IsTheHeaderOrTheEmptyNotice()
    {
        var csv = await Exports.ExportCsvAsync(Admin(SeedAdminId), ConcertId, Ct);
        var html = await Exports.ExportHtmlAsync(Admin(SeedAdminId), ConcertId, Ct);

        Assert.Single(Encoding.UTF8.GetString(csv.Content).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("Bu etkinlikte kayıtlı katılımcı yok.", Encoding.UTF8.GetString(html.Content), StringComparison.Ordinal);
    }
}