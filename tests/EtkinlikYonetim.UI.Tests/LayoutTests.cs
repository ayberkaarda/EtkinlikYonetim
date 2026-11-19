using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Tests.Support;
using EtkinlikYonetim.UI.Theming;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>
/// Every screen keeps each control inside its parent, without overlaps, on a small laptop, a common laptop
/// and a large screen at four text sizes; long event and person names never hide seats or a state.
/// Nothing is shown.
/// </summary>
public sealed class LayoutTests
{
    public static TheoryData<string> ScreenNames => [.. TestForms.Names];

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_FitsEveryScreenAtEveryTextSize(string name) => Run(() =>
        LayoutAssert.FitsInsideOnEveryScreen(() => TestForms.Create(name)));

    /// <summary>A long event name gives way to its pill; the date and the seats lead the second line.</summary>
    [Fact]
    public void LongEventNames_KeepTheSeatsAndTheState()
    {
        var day = new DateOnly(2027, 6, 12);

        var open = EventRows.Describe(new EventSummary(1, TestForms.LongEvent, day, 400, 251));
        var full = EventRows.Describe(new EventSummary(1, TestForms.LongEvent, day, 400, 400, 12));
        var cancelled = EventRows.Describe(new EventSummary(1, TestForms.LongEvent, day, 400, 9, Cancelled: true));
        var past = EventRows.Describe(new EventSummary(1, TestForms.LongEvent, day, 400, 9), today: day.AddDays(1));

        Assert.Equal(("149 boş yer", RowTone.Accent), (open.Status, open.StatusTone));
        Assert.Equal(("Dolu", RowTone.Warm), (full.Status, full.StatusTone));
        Assert.Equal(("İptal edildi", RowTone.Warning), (cancelled.Status, cancelled.StatusTone));
        Assert.Equal("Geçti", past.Status);
        Assert.Equal("12.06.2027 · 251/400 kayıt", open.Secondary);
        Assert.Equal("12.06.2027 · 400/400 kayıt · 12 kişi bekliyor", full.Secondary);
    }

    [Fact]
    public void Registrations_TellTicketWaitingPlaceAndCancellationApart()
    {
        var active = new RegistrationItem(Sessions.Registration(5) with { ParticipantName = TestForms.LongName }).Row();
        var waiting = new RegistrationItem(Sessions.Registration(6) with { TicketCode = null, Status = RegistrationStatus.Waiting, QueuePosition = 3 }).Row();
        var cancelled = new RegistrationItem(Sessions.Registration(7) with { Status = RegistrationStatus.EventCancelled }).Row();

        Assert.Equal(("Bilet hazır", RowTone.Accent), (active.Status, active.StatusTone));
        Assert.Equal(("Bekleme listesi · 3. sıra", RowTone.Warm), (waiting.Status, waiting.StatusTone));
        Assert.Equal(("Etkinlik iptal edildi", RowTone.Warning), (cancelled.Status, cancelled.StatusTone));
        Assert.Equal("17.04.2027 · " + TestForms.LongName, active.Secondary);
        Assert.DoesNotContain("ETK-", active.Primary + active.Secondary + active.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void ParticipantsAndAccounts_KeepTheirState()
    {
        var inside = new ParticipantItem(new ParticipantView(TestForms.LongName, "100******46", "a@example.com", "Giriş yapıldı")).Row();
        var waiting = new ParticipantItem(new ParticipantView("Ece Taslak", "100******14", "ece@example.com", "Bekleme listesi, sıra: 2")).Row();
        var cancelled = new ParticipantItem(new ParticipantView("Ece Taslak", "100******14", "ece@example.com", "Etkinlik iptal edildi")).Row();
        var account = new AccountItem(new AccountSummary(3, "aysegulkaramanlioglubuyukcekmeceli", UserRole.User, true)).Row();

        Assert.Equal(("Giriş yapıldı", RowTone.Accent), (inside.Status, inside.StatusTone));
        Assert.Equal(RowTone.Warm, waiting.StatusTone);
        Assert.Equal(RowTone.Warning, cancelled.StatusTone);
        Assert.Equal("100******46 · a@example.com", inside.Secondary);
        Assert.Equal(("Devre dışı", RowTone.Warning, "Kullanıcı"), (account.Status, account.StatusTone, account.Secondary));
    }

    /// <summary>The pill keeps its whole width at the right of a narrow line; the name gets what is left, never the pill's room.</summary>
    [Theory]
    [InlineData(400, 90)]
    [InlineData(120, 90)]
    [InlineData(60, 90)]
    public void StatusPill_NeverGivesWayToTheName(int lineWidth, int pillWidth)
    {
        var line = new Rectangle(10, 5, lineWidth, 20);

        var (primary, status) = ThemedListBox.SplitLine(line, pillWidth, 8);

        Assert.Equal(line.Right, status.Right);
        Assert.Equal(Math.Min(pillWidth, lineWidth), status.Width);
        Assert.False(primary.IntersectsWith(status));
        Assert.True(primary.Width >= 0);
        Assert.Equal(line, ThemedListBox.SplitLine(line, 0, 8).Primary);
    }

    /// <summary>A message window never opens larger than the working area of its screen; its content scrolls instead.</summary>
    [Fact]
    public void MessageDialog_StaysInsideASmallWorkingArea() => Run(() =>
    {
        var previous = AppDialog.WorkingAreaOf;
        var area = new Rectangle(0, 0, 400, 240);
        AppDialog.WorkingAreaOf = _ => area;
        try
        {
            var message = string.Join(" ", Enumerable.Repeat("Bu uzun bir açıklama satırıdır.", 60));
            using var dialog = new MessageDialog(message, "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            typeof(Form).GetMethod("OnLoad", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog, [EventArgs.Empty]);

            Assert.True(dialog.Width <= area.Width && dialog.Height <= area.Height, $"The dialog opens at {dialog.Size}.");
            Assert.True(dialog.AutoScroll);
        }
        finally
        {
            AppDialog.WorkingAreaOf = previous;
        }
    });
}
