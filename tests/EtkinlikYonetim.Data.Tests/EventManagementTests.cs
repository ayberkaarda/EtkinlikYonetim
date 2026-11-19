using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>Changing and cancelling events end to end on SQLite, with their registrations and waiting lists.</summary>
public sealed class EventManagementTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Today => Start.UtcDateTime.Date;

    private IEventService Events => _db.Get<IEventService>();

    private IRegistrationService Registrations => _db.Get<IRegistrationService>();

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Update_StoresTheDateCapacityAndDescription()
    {
        await Events.UpdateAsync(Admin(SeedAdminId), ConcertId, new DateTime(2027, 5, 1), 200, "  Açık hava sahnesi  ", Ct);

        var stored = (await Events.GetEventsAsync(Admin(SeedAdminId), Ct)).Single(e => e.Id == ConcertId);
        Assert.Equal((new DateOnly(2027, 5, 1), 200, "Açık hava sahnesi", "Bahar Konseri"), (stored.Date, stored.Capacity, stored.Description, stored.Name));
    }

    /// <summary>The capacity may go down to the seats held (a full event) but not below; nothing changes when it is refused.</summary>
    [Fact]
    public async Task Update_CapacityDownToTheSeatsHeldButNotBelow()
    {
        var admin = Admin(SeedAdminId);
        var id = await Events.CreateAsync(admin, "Kapasite", Today.AddDays(5), 3, Ct);
        await Registrations.RegisterAsync(admin, id, DenizNationalId, Ct);
        await Registrations.RegisterAsync(admin, id, EceNationalId, Ct);

        var below = await Assert.ThrowsAsync<BusinessRuleException>(() => Events.UpdateAsync(admin, id, Today.AddDays(5), 1, null, Ct));
        Assert.Equal(EventService.CapacityBelowRegistrations, below.Message);
        Assert.Equal(3, (await Events.GetEventsAsync(admin, Ct)).Single(e => e.Id == id).Capacity);

        await Events.UpdateAsync(admin, id, Today.AddDays(5), 2, null, Ct);
        var full = (await Events.GetEventsAsync(admin, Ct)).Single(e => e.Id == id);
        Assert.Equal((2, 2, 0), (full.Capacity, full.RegisteredCount, full.FreeSeats));
        Assert.True((await Registrations.RegisterAsync(admin, id, MertNationalId, Ct)).Waitlisted);
    }

    /// <summary>Capacity 0 is not an event that only has a waiting list: it is refused like any value outside 1 to 1000.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task Update_CapacityOutsideTheLimits_IsRefused(int capacity)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Events.UpdateAsync(Admin(SeedAdminId), SmallEventId, Today.AddDays(5), capacity, null, Ct));

        Assert.Equal(EventService.CapacityInvalid, error.Message);
    }

    /// <summary>More seats go to the waiting list at once, in its order, each with a ticket.</summary>
    [Fact]
    public async Task Update_LargerCapacity_PromotesTheWaitingInOrder()
    {
        var admin = Admin(SeedAdminId);
        var id = await Events.CreateAsync(admin, "Büyüyen", Today.AddDays(5), 1, Ct);
        await Registrations.RegisterAsync(admin, id, DenizNationalId, Ct);
        var first = await Registrations.RegisterAsync(admin, id, EceNationalId, Ct);
        var second = await Registrations.RegisterAsync(admin, id, MertNationalId, Ct);

        await Events.UpdateAsync(admin, id, Today.AddDays(5), 2, null, Ct);

        var mine = (await Registrations.GetMyRegistrationsAsync(admin, Ct)).Where(r => r.EventId == id).ToDictionary(r => r.Id);
        Assert.Equal(RegistrationStatus.Active, mine[first.RegistrationId].Status);
        Assert.NotNull(mine[first.RegistrationId].TicketCode);
        Assert.Equal((RegistrationStatus.Waiting, 1), (mine[second.RegistrationId].Status, mine[second.RegistrationId].QueuePosition!.Value));
        Assert.Equal(2, (await Events.GetEventsAsync(admin, Ct)).Single(e => e.Id == id).RegisteredCount);
    }

    /// <summary>A past event can neither be changed nor cancelled, and a new date cannot be in the past.</summary>
    [Fact]
    public async Task PastEvent_CannotBeChangedOrCancelled()
    {
        var admin = Admin(SeedAdminId);
        var id = await Events.CreateAsync(admin, "Bugün", Today, 5, Ct);
        _db.Clock.Advance(TimeSpan.FromDays(1));

        var update = await Assert.ThrowsAsync<BusinessRuleException>(() => Events.UpdateAsync(admin, id, Today.AddDays(10), 5, null, Ct));
        var cancel = await Assert.ThrowsAsync<BusinessRuleException>(() => Events.CancelAsync(admin, id, Ct));
        var newDate = await Assert.ThrowsAsync<BusinessRuleException>(() => Events.UpdateAsync(admin, ConcertId, Today, 5, null, Ct));

        Assert.Equal(EventService.EventClosed, update.Message);
        Assert.Equal(EventService.EventClosed, cancel.Message);
        Assert.Equal(EventService.NewDateInPast, newDate.Message);
        var stored = (await Events.GetEventsAsync(admin, Ct)).Single(e => e.Id == id);
        Assert.Equal((DateOnly.FromDateTime(Today), false), (stored.Date, stored.Cancelled));
    }

    /// <summary>
    /// Cancelling is a soft delete: the event stays listed as cancelled, its seats and waiting places are
    /// closed and shown to their accounts as cancelled, the counter goes to zero, and nothing can be
    /// registered, cancelled or changed any more.
    /// </summary>
    [Fact]
    public async Task Cancel_ClosesRegistrationsAndKeepsTheEvent()
    {
        var admin = Admin(SeedAdminId);
        var user = User(SeedUserId);
        var seat = await Registrations.RegisterAsync(user, SmallEventId, DenizNationalId, Ct);
        await Registrations.RegisterAsync(user, SmallEventId, EceNationalId, Ct);
        await Registrations.RegisterAsync(admin, SmallEventId, MertNationalId, Ct); // waits

        await Events.CancelAsync(admin, SmallEventId, Ct);

        var all = await Events.GetEventsAsync(user, Ct);
        var cancelled = all.Single(e => e.Id == SmallEventId);
        Assert.True(cancelled.Cancelled);
        Assert.Equal((0, 0), (cancelled.RegisteredCount, cancelled.WaitingCount));
        Assert.EndsWith("(İptal edildi)", cancelled.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(await Events.GetOpenEventsAsync(user, Ct), e => e.Id == SmallEventId);

        var mine = await Registrations.GetMyRegistrationsAsync(user, Ct);
        Assert.Equal(2, mine.Count);
        Assert.All(mine, r => Assert.Equal(RegistrationStatus.EventCancelled, r.Status));

        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(user, SmallEventId, DenizNationalId, Ct));
        Assert.Equal(RegistrationService.CancelledEvent, again.Message);
        var cancel = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.CancelAsync(user, seat.RegistrationId, Ct));
        Assert.Equal(RegistrationService.AlreadyCancelled, cancel.Message);
        var twice = await Assert.ThrowsAsync<BusinessRuleException>(() => Events.CancelAsync(admin, SmallEventId, Ct));
        Assert.Equal(EventService.EventClosed, twice.Message);
        var change = await Assert.ThrowsAsync<BusinessRuleException>(() => Events.UpdateAsync(admin, SmallEventId, Today.AddDays(400), 5, null, Ct));
        Assert.Equal(EventService.EventClosed, change.Message);

        await using var context = _db.CreateContext();
        Assert.Equal(3, await context.Registrations.CountAsync(r => r.EventId == SmallEventId && r.Status == RegistrationStatus.EventCancelled, Ct));
        Assert.True(await context.Events.AnyAsync(e => e.Id == SmallEventId, Ct));
    }

    [Fact]
    public async Task UnknownEvent_IsReportedAsNotFound()
    {
        var update = await Assert.ThrowsAsync<BusinessRuleException>(() => Events.UpdateAsync(Admin(SeedAdminId), 999, Today, 5, null, Ct));
        var cancel = await Assert.ThrowsAsync<BusinessRuleException>(() => Events.CancelAsync(Admin(SeedAdminId), 999, Ct));

        Assert.Equal(EventService.EventNotFound, update.Message);
        Assert.Equal(EventService.EventNotFound, cancel.Message);
    }
}
