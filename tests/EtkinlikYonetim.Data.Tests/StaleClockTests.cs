using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Repositories;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// The time a write decides with is read inside its transaction, after its locks. Here the clock stands one
/// second before midnight when an operation starts, and the day ends while the operation waits to write (on
/// SQL Server: for the event's lock). On SQLite the fake clock is the database's clock, so the tests move it
/// right before the repository's write begins.
/// </summary>
public sealed class StaleClockTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Register_DayEndsBeforeTheWrite_IsRefused()
    {
        var (app, midnight, eventId, _) = await PrepareAsync();
        await using var services = app;

        midnight.Armed = true;
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(
            () => app.GetRequiredService<IRegistrationService>().RegisterAsync(User(SeedUserId), eventId, EceNationalId, Ct));

        Assert.Equal(RegistrationService.PastEvent, refused.Message);
        Assert.False(midnight.Armed);
        await using var context = _db.CreateContext();
        Assert.Equal(1, await context.Registrations.CountAsync(r => r.EventId == eventId, Ct));
    }

    [Fact]
    public async Task CancelRegistration_DayEndsBeforeTheWrite_IsRefused()
    {
        var (app, midnight, _, registrationId) = await PrepareAsync();
        await using var services = app;

        midnight.Armed = true;
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => app.GetRequiredService<IRegistrationService>().CancelAsync(User(SeedUserId), registrationId, Ct));

        Assert.False(midnight.Armed);
        await using var context = _db.CreateContext();
        Assert.Equal(RegistrationStatus.Active, (await context.Registrations.SingleAsync(r => r.Id == registrationId, Ct)).Status);
    }

    [Fact]
    public async Task ChangeEvent_DayEndsBeforeTheWrite_IsRefused()
    {
        var (app, midnight, eventId, _) = await PrepareAsync();
        await using var services = app;
        var events = app.GetRequiredService<IEventService>();
        var loaded = (await events.GetEventsAsync(Admin(SeedAdminId), Ct)).Single(e => e.Id == eventId);

        midnight.Armed = true;
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(
            () => events.UpdateAsync(Admin(SeedAdminId), loaded, loaded.Date.ToDateTime(TimeOnly.MinValue), 9, "Geç", Ct));

        Assert.Equal(EventService.EventClosed, refused.Message);
        await using var context = _db.CreateContext();
        Assert.Equal(5, (await context.Events.SingleAsync(e => e.Id == eventId, Ct)).Capacity);
    }

    [Fact]
    public async Task CancelEvent_DayEndsBeforeTheWrite_IsRefused()
    {
        var (app, midnight, eventId, _) = await PrepareAsync();
        await using var services = app;

        midnight.Armed = true;
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(
            () => app.GetRequiredService<IEventService>().CancelAsync(Admin(SeedAdminId), eventId, Ct));

        Assert.Equal(EventService.EventClosed, refused.Message);
        await using var context = _db.CreateContext();
        Assert.False((await context.Events.SingleAsync(e => e.Id == eventId, Ct)).Cancelled);
    }

    /// <summary>
    /// An event of today with one seat taken by the user's participant, the clock one second before
    /// midnight, and services whose event and registration writes start two seconds late once armed.
    /// </summary>
    private async Task<(ServiceProvider App, Midnight Midnight, int EventId, int RegistrationId)> PrepareAsync()
    {
        var day = DateOnly.FromDateTime(_db.Clock.GetUtcNow().UtcDateTime);
        _db.Clock.SetUtcNow(new DateTimeOffset(day.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero));
        var midnight = new Midnight(_db.Clock);
        var app = _db.BuildServices(services =>
        {
            services.AddSingleton(provider => BeforeCall<IEventRepository>.Wrap(ActivatorUtilities.CreateInstance<EventRepository>(provider), IsWrite, midnight.BeforeWriteAsync));
            services.AddSingleton(provider => BeforeCall<IRegistrationRepository>.Wrap(ActivatorUtilities.CreateInstance<RegistrationRepository>(provider), IsWrite, midnight.BeforeWriteAsync));
        });
        var eventId = await app.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Gece yarısı", day.ToDateTime(TimeOnly.MinValue), 5, Ct);
        var receipt = await app.GetRequiredService<IRegistrationService>().RegisterAsync(User(SeedUserId), eventId, DenizNationalId, Ct);
        return (app, midnight, eventId, receipt.RegistrationId);
    }

    /// <summary>The repository writes: adding, changing and cancelling.</summary>
    private static bool IsWrite(string method) => method is "AddAsync" or "UpdateAsync" or "CancelAsync";

    /// <summary>Once armed, lets two seconds pass on the clock the next time a write is about to start.</summary>
    private sealed class Midnight(FakeTimeProvider clock)
    {
        public bool Armed { get; set; }

        public Task BeforeWriteAsync()
        {
            if (Armed)
            {
                Armed = false;
                clock.Advance(TimeSpan.FromSeconds(2));
            }

            return Task.CompletedTask;
        }
    }
}
