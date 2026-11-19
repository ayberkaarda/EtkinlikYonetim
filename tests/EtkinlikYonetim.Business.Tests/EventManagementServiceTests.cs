using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>Changing and cancelling events: admins only, inputs checked first, the repository's verdict translated.</summary>
public sealed class EventManagementServiceTests
{
    // 2 March 2026, local time zone of the fake clock is UTC.
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 3, 2);

    private readonly Mock<IEventRepository> _events = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingAudit _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UpdateAndCancel_ByAUser_AreDeniedWithoutTouchingTheRepository()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().UpdateAsync(Sessions.User, Loaded(1), Now.Date, 10, null, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().CancelAsync(Sessions.User, 1, Ct));

        _events.VerifyNoOtherCalls();
        Assert.Equal([(AuditEvent.AccessDenied, UserRole.User), (AuditEvent.AccessDenied, UserRole.User)], _audit.Events);
    }

    [Theory]
    [InlineData(-1, 10, 0, nameof(EventService.NewDateInPast))]
    [InlineData(0, 0, 0, nameof(EventService.CapacityInvalid))]
    [InlineData(0, 1001, 0, nameof(EventService.CapacityInvalid))]
    [InlineData(0, 10, 501, nameof(EventService.DescriptionTooLong))]
    public async Task Update_InvalidInput_IsRejectedBeforeWriting(int days, int capacity, int descriptionLength, string expected)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().UpdateAsync(Sessions.Admin, Loaded(1), Now.Date.AddDays(days), capacity, new string('a', descriptionLength), Ct));

        Assert.Equal(Message(expected), error.Message);
        _events.VerifyNoOtherCalls();
    }

    /// <summary>Today, capacity 1 and a 500 character description are the limits that are still accepted; the text is trimmed.</summary>
    [Fact]
    public async Task Update_AtTheLimits_IsPassedOnTrimmed()
    {
        var description = " " + new string('a', 500) + " ";
        _events.Setup(e => e.UpdateAsync(It.IsAny<Actor>(), 1, Expected(1), new EventValues(Today, 1, description.Trim()), Today, Now.UtcDateTime, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventChangeStatus.Changed);
        _events.Setup(e => e.UpdateAsync(It.IsAny<Actor>(), 2, Expected(2), new EventValues(Today.AddDays(3), 1000, null), Today, Now.UtcDateTime, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventChangeStatus.Changed);

        await Service().UpdateAsync(Sessions.Admin, Loaded(1), Now.Date.AddHours(20), 1, description, Ct);
        await Service().UpdateAsync(Sessions.Admin, Loaded(2), Now.Date.AddDays(3), 1000, "   ", Ct);

        Assert.Equal([(AuditEvent.EventUpdated, UserRole.Admin), (AuditEvent.EventUpdated, UserRole.Admin)], _audit.Events);
    }

    [Theory]
    [InlineData(EventChangeStatus.Closed, nameof(EventService.EventClosed))]
    [InlineData(EventChangeStatus.CapacityBelowRegistrations, nameof(EventService.CapacityBelowRegistrations))]
    [InlineData(EventChangeStatus.NotFound, nameof(EventService.EventNotFound))]
    public async Task Update_RefusedByTheRepository_IsReportedAndNotAudited(EventChangeStatus status, string expected)
    {
        _events.Setup(e => e.UpdateAsync(It.IsAny<Actor>(), 1, Expected(1), new EventValues(Today, 5, null), Today, Now.UtcDateTime, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().UpdateAsync(Sessions.Admin, Loaded(1), Now.Date, 5, null, Ct));

        Assert.Equal(Message(expected), error.Message);
        Assert.Empty(_audit.Events);
    }

    /// <summary>
    /// The repository found the event no longer as the admin loaded it: the answer is its own exception, so
    /// the window can load the event again, with a fixed message; nothing is audited.
    /// </summary>
    [Fact]
    public async Task Update_EventChangedElsewhere_IsReportedAsStale()
    {
        _events.Setup(e => e.UpdateAsync(It.IsAny<Actor>(), 1, Expected(1), new EventValues(Today, 5, null), Today, Now.UtcDateTime, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventChangeStatus.ChangedElsewhere);

        var error = await Assert.ThrowsAsync<StaleDataException>(() => Service().UpdateAsync(Sessions.Admin, Loaded(1), Now.Date, 5, null, Ct));

        Assert.Equal(EventService.ChangedElsewhere, error.Message);
        Assert.Empty(_audit.Events);
    }

    [Theory]
    [InlineData(EventChangeStatus.Closed, nameof(EventService.EventClosed))]
    [InlineData(EventChangeStatus.NotFound, nameof(EventService.EventNotFound))]
    public async Task Cancel_RefusedByTheRepository_IsReported(EventChangeStatus status, string expected)
    {
        _events.Setup(e => e.CancelAsync(It.IsAny<Actor>(), 1, Today, Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(status);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CancelAsync(Sessions.Admin, 1, Ct));

        Assert.Equal(Message(expected), error.Message);
        Assert.Empty(_audit.Events);
    }

    [Fact]
    public async Task Cancel_IsAudited()
    {
        _events.Setup(e => e.CancelAsync(It.IsAny<Actor>(), 1, Today, Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(EventChangeStatus.Changed);

        await Service().CancelAsync(Sessions.Admin, 1, Ct);

        Assert.Equal([(AuditEvent.EventCancelled, UserRole.Admin)], _audit.Events);
    }

    /// <summary>
    /// The write goes out as the session's account (id, role and session version), and when the repository
    /// finds that account changed after its lock, the session ends like one the guard rejects.
    /// </summary>
    [Fact]
    public async Task Cancel_ActorRevokedInsideTheWrite_EndsTheSession()
    {
        var session = new UserSession(4, "ikinci", UserRole.Admin, sessionVersion: 3);
        _events.Setup(e => e.CancelAsync(new Actor(4, UserRole.Admin, 3), 1, Today, Now.UtcDateTime, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ActorRevokedException());

        var error = await Assert.ThrowsAsync<SessionExpiredException>(() => Service().CancelAsync(session, 1, Ct));

        Assert.IsType<ActorRevokedException>(error.InnerException);
        Assert.Equal([(AuditEvent.SessionRejected, UserRole.Admin)], _audit.Events);
    }

    [Fact]
    public void Messages_AreFixedTexts()
    {
        Assert.Equal("Etkinliğin yeni tarihi geçmiş bir gün olamaz.", EventService.NewDateInPast);
        Assert.Equal("Geçmiş veya iptal edilmiş bir etkinlik değiştirilemez ya da iptal edilemez.", EventService.EventClosed);
        Assert.Equal("Kapasite, etkinliğin mevcut kayıt sayısının altına indirilemez.", EventService.CapacityBelowRegistrations);
        Assert.Equal("Açıklama en fazla 500 karakter olabilir.", EventService.DescriptionTooLong);
        Assert.Equal("İptal edilmiş bir etkinliğe kayıt yapılamaz.", RegistrationService.CancelledEvent);
    }

    private static string Message(string name) => name switch
    {
        nameof(EventService.NewDateInPast) => EventService.NewDateInPast,
        nameof(EventService.CapacityInvalid) => EventService.CapacityInvalid,
        nameof(EventService.DescriptionTooLong) => EventService.DescriptionTooLong,
        nameof(EventService.EventClosed) => EventService.EventClosed,
        nameof(EventService.CapacityBelowRegistrations) => EventService.CapacityBelowRegistrations,
        nameof(EventService.EventNotFound) => EventService.EventNotFound,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>Event <paramref name="id"/> as an admin's window loaded it.</summary>
    private static EventSummary Loaded(int id) => new(id, "Konser " + id, Today.AddDays(10), 50, 0, 0, id == 1 ? "Eski açıklama" : null);

    private static EventValues Expected(int id) => new(Loaded(id).Date, Loaded(id).Capacity, Loaded(id).Description);

    private EventService Service() => ServiceFactory.Event(_events.Object, _time, _audit);
}
