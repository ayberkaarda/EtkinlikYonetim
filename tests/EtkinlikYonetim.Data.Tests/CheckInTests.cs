using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>Tickets and the entrance end to end on SQLite.</summary>
public sealed class CheckInTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ICheckInService CheckIns => _db.Get<ICheckInService>();

    private IRegistrationService Registrations => _db.Get<IRegistrationService>();

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    /// <summary>
    /// Decision: the code is stored as it is, next to its SHA-256. The participant's account must be able
    /// to show the QR code again, which a hash alone cannot give back; the entrance looks the ticket up by
    /// the hash and then compares the code in constant time.
    /// </summary>
    [Fact]
    public async Task Ticket_IsStoredWithItsCodeForTheQrAndItsHashForTheLookup()
    {
        var receipt = await Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct);

        await using var context = _db.CreateContext();
        var ticket = await context.Tickets.SingleAsync(Ct);
        Assert.Equal(receipt.TicketCode, ticket.Code);
        Assert.Equal(TicketCodeHash.Of(receipt.TicketCode!), ticket.CodeHash);
        Assert.Null(ticket.CheckedInAt);
        var mine = await Registrations.GetMyRegistrationsAsync(User(SeedUserId), Ct);
        Assert.Equal(receipt.TicketCode, Assert.Single(mine).TicketCode); // shown again to its own account

        // An admin sees the registration, but the code only of participants the admin added or that belong to no account.
        Assert.Null(Assert.Single(await Registrations.GetMyRegistrationsAsync(Admin(SeedAdminId), Ct)).TicketCode);
    }

    /// <summary>The first scan checks in, the second says "already used" on the same screen; the counter follows.</summary>
    [Fact]
    public async Task CheckIn_FirstScanEntersSecondIsAlreadyUsed()
    {
        var admin = Admin(SeedAdminId);
        var receipt = await Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct);
        await Registrations.RegisterAsync(User(SeedUserId), ConcertId, EceNationalId, Ct);
        Assert.Equal(new CheckInCount(0, 2), await CheckIns.GetCountAsync(admin, ConcertId, Ct));

        var first = await CheckIns.CheckInAsync(admin, ConcertId, receipt.TicketCode, Ct);
        var second = await CheckIns.CheckInAsync(admin, ConcertId, receipt.TicketCode, Ct);

        Assert.Equal(new CheckInResult(CheckInOutcome.CheckedIn, new CheckInCount(1, 2)), first);
        Assert.Equal(new CheckInResult(CheckInOutcome.AlreadyUsed, new CheckInCount(1, 2)), second);
        await using var context = _db.CreateContext();
        Assert.Equal(Start.UtcDateTime, (await context.Tickets.SingleAsync(t => t.Code == receipt.TicketCode, Ct)).CheckedInAt);
    }

    [Fact]
    public async Task CheckIn_TicketOfAnotherEvent_IsRejected()
    {
        var receipt = await Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct);

        var result = await CheckIns.CheckInAsync(Admin(SeedAdminId), SmallEventId, receipt.TicketCode, Ct);

        Assert.Equal(CheckInOutcome.Rejected, result.Outcome);
        Assert.Equal(new CheckInCount(0, 0), result.Count); // an event without registrations
        await using var context = _db.CreateContext();
        Assert.Null((await context.Tickets.SingleAsync(Ct)).CheckedInAt);
    }

    [Fact]
    public async Task CheckIn_TicketOfACancelledRegistrationOrEvent_IsRejected()
    {
        var admin = Admin(SeedAdminId);
        var cancelled = await Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct);
        await Registrations.CancelAsync(User(SeedUserId), cancelled.RegistrationId, Ct);
        var closed = await Registrations.RegisterAsync(User(SeedUserId), SmallEventId, EceNationalId, Ct);
        await _db.Get<IEventService>().CancelAsync(admin, SmallEventId, Ct);

        Assert.Equal(CheckInOutcome.Rejected, (await CheckIns.CheckInAsync(admin, ConcertId, cancelled.TicketCode, Ct)).Outcome);
        Assert.Equal(CheckInOutcome.Rejected, (await CheckIns.CheckInAsync(admin, SmallEventId, closed.TicketCode, Ct)).Outcome);
        Assert.Equal(CheckInOutcome.Rejected, (await CheckIns.CheckInAsync(admin, ConcertId, "ETK-" + new string('A', 26), Ct)).Outcome);
    }

    /// <summary>
    /// The stored code itself decides, compared in constant time, not only the hash that found the row:
    /// a row whose code was changed behind the application's back no longer lets its hash in.
    /// </summary>
    [Fact]
    public async Task CheckIn_StoredCodeChanged_IsRejectedAlthoughTheHashIsFound()
    {
        var receipt = await Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct);
        await using (var context = _db.CreateContext())
        {
            await context.Tickets.ExecuteUpdateAsync(s => s.SetProperty(t => t.Code, "ETK-" + new string('B', 26)), Ct);
        }

        Assert.NotNull(await _db.Get<ITicketRepository>().FindForCheckInAsync(TicketCodeHash.Of(receipt.TicketCode!), Ct));
        Assert.Equal(CheckInOutcome.Rejected, (await CheckIns.CheckInAsync(Admin(SeedAdminId), ConcertId, receipt.TicketCode, Ct)).Outcome);
    }

    /// <summary>
    /// A participant who has been let in cannot be cancelled afterwards, neither through the service nor by a
    /// repository call that skips its checks: the seat stays taken, the counter says so and the one waiting
    /// stays waiting, so the room never holds more people than the event has seats.
    /// </summary>
    [Fact]
    public async Task Cancel_AfterCheckIn_IsRefusedAndTheSeatStaysTaken()
    {
        var admin = Admin(SeedAdminId);
        var id = await _db.Get<IEventService>().CreateAsync(admin, "Tek", Start.UtcDateTime.Date, 1, Ct);
        var inside = await Registrations.RegisterAsync(User(SeedUserId), id, DenizNationalId, Ct);
        var waiting = await Registrations.RegisterAsync(User(SeedUserId), id, EceNationalId, Ct);
        Assert.Equal(CheckInOutcome.CheckedIn, (await CheckIns.CheckInAsync(admin, id, inside.TicketCode, Ct)).Outcome);

        var byUser = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.CancelAsync(User(SeedUserId), inside.RegistrationId, Ct));
        var byAdmin = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.CancelAsync(admin, inside.RegistrationId, Ct));
        var direct = await _db.Get<IRegistrationRepository>().CancelAsync(
            inside.RegistrationId, null, DateOnly.FromDateTime(Start.UtcDateTime), Start.UtcDateTime, () => "ETK-" + new string('C', 26), Ct);

        Assert.Equal(RegistrationService.CheckedInCannotCancel, byUser.Message);
        Assert.Equal(RegistrationService.CheckedInCannotCancel, byAdmin.Message);
        Assert.Equal(CancellationOutcome.NotCancelled, direct);
        Assert.Equal(new CheckInCount(1, 1), await CheckIns.GetCountAsync(admin, id, Ct));
        await using var context = _db.CreateContext();
        Assert.Equal(RegistrationStatus.Active, (await context.Registrations.SingleAsync(r => r.Id == inside.RegistrationId, Ct)).Status);
        Assert.Equal(RegistrationStatus.Waiting, (await context.Registrations.SingleAsync(r => r.Id == waiting.RegistrationId, Ct)).Status);
        Assert.Equal(1, (await context.Events.SingleAsync(e => e.Id == id, Ct)).RegisteredCount);
    }

    /// <summary>A participant who moves up from the waiting list gets a ticket that works at the entrance.</summary>
    [Fact]
    public async Task CheckIn_TicketOfAPromotedParticipant_Works()
    {
        var admin = Admin(SeedAdminId);
        var id = await _db.Get<IEventService>().CreateAsync(admin, "Tek", Start.UtcDateTime.Date.AddDays(3), 1, Ct);
        var first = await Registrations.RegisterAsync(admin, id, DenizNationalId, Ct);
        var waiting = await Registrations.RegisterAsync(admin, id, EceNationalId, Ct);
        await Registrations.CancelAsync(admin, first.RegistrationId, Ct);

        // Ece belongs to the user account, which is the one that sees her new ticket.
        var promoted = (await Registrations.GetMyRegistrationsAsync(User(SeedUserId), Ct)).Single(r => r.Id == waiting.RegistrationId);

        Assert.Equal(CheckInOutcome.CheckedIn, (await CheckIns.CheckInAsync(admin, id, promoted.TicketCode, Ct)).Outcome);
        Assert.Equal(CheckInOutcome.Rejected, (await CheckIns.CheckInAsync(admin, id, first.TicketCode, Ct)).Outcome);
    }
}
