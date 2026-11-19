using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace EtkinlikYonetim.Business.Tests;

public sealed class EventServiceTests
{
    // 2 March 2026, local time zone of the fake clock is UTC.
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 23, 30, 0, TimeSpan.Zero);

    private readonly Mock<IEventRepository> _events = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_ByAUser_IsDeniedWithoutTouchingTheRepository()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().CreateAsync(Sessions.User, "Konser", new DateTime(2026, 4, 1), 10, Ct));

        _events.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", 10, 0, nameof(EventService.NameRequired))]
    [InlineData("   ", 10, 0, nameof(EventService.NameRequired))]
    [InlineData("Konser", 10, -1, nameof(EventService.PastDate))]
    [InlineData("Konser", 0, 0, nameof(EventService.CapacityInvalid))]
    [InlineData("Konser", 1001, 0, nameof(EventService.CapacityInvalid))]
    public async Task Create_InvalidInput_IsRejected(string name, int capacity, int daysFromToday, string expected)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().CreateAsync(Sessions.Admin, name, Now.Date.AddDays(daysFromToday), capacity, Ct));

        Assert.Equal(Message(expected), error.Message);
        _events.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_NameLongerThanTheField_IsRejected()
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().CreateAsync(Sessions.Admin, new string('a', 81), Now.Date, 10, Ct));

        Assert.Equal(EventService.NameTooLong, error.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task Create_TodayAndCapacityBounds_AreStoredTrimmedWithoutRegistrations(int capacity)
    {
        ScheduledEvent? added = null;
        _events.Setup(e => e.AddAsync(It.IsAny<ScheduledEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledEvent, CancellationToken>((e, _) => added = e)
            .ReturnsAsync(4);

        var id = await Service().CreateAsync(Sessions.Admin, " Konser ", Now.Date.AddHours(5), capacity, Ct);

        Assert.Equal(4, id);
        Assert.NotNull(added);
        Assert.Equal("Konser", added.Name);
        Assert.Equal(new DateOnly(2026, 3, 2), added.Date);
        Assert.Equal(capacity, added.Capacity);
        Assert.Equal(0, added.RegisteredCount);
    }

    [Fact]
    public async Task OpenEvents_StartToday()
    {
        _events.Setup(e => e.GetFromAsync(new DateOnly(2026, 3, 2), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Service().GetOpenEventsAsync(Sessions.User, Ct);

        _events.Verify(e => e.GetFromAsync(new DateOnly(2026, 3, 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static string Message(string name) => name switch
    {
        nameof(EventService.NameRequired) => EventService.NameRequired,
        nameof(EventService.PastDate) => EventService.PastDate,
        nameof(EventService.CapacityInvalid) => EventService.CapacityInvalid,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private EventService Service() => ServiceFactory.Event(_events.Object, _time);
}
