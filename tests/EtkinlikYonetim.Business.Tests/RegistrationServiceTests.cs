using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>Registration rules and ownership (IDOR) checks with strict mocks.</summary>
public sealed class RegistrationServiceTests
{
    private const string OwnNationalId = "10000000146";
    private const string ForeignNationalId = "10000000214";
    private const string LegacyNationalId = "20000000001";
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 10, 10, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 3, 2);

    private readonly Mock<IRegistrationRepository> _registrations = new(MockBehavior.Strict);
    private readonly Mock<IParticipantRepository> _participants = new(MockBehavior.Strict);
    private readonly Mock<IEventRepository> _events = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(Now);

    public RegistrationServiceTests()
    {
        _participants.Setup(p => p.FindByNationalIdAsync(OwnNationalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantRecord(1, OwnNationalId, "Deniz Örnek", null, Sessions.User.UserId));
        _participants.Setup(p => p.FindByNationalIdAsync(ForeignNationalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantRecord(2, ForeignNationalId, "Ece Taslak", null, Sessions.OtherUser.UserId));
        _participants.Setup(p => p.FindByNationalIdAsync(LegacyNationalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantRecord(3, LegacyNationalId, "Eski Katılımcı", null, null));
        _participants.Setup(p => p.FindByNationalIdAsync("10000000382", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantRecord?)null);
        _events.Setup(e => e.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new EventSummary(1, "Konser", Today, 2, 1));
        _events.Setup(e => e.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(new EventSummary(2, "Dolu", Today.AddDays(3), 2, 2));
        _events.Setup(e => e.GetByIdAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(new EventSummary(3, "Dün", Today.AddDays(-1), 2, 0));
        _events.Setup(e => e.GetByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((EventSummary?)null);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0, OwnNationalId, nameof(RegistrationService.EventRequired))]
    [InlineData(1, "", nameof(RegistrationService.NationalIdFormat))]
    [InlineData(1, "1000000014", nameof(RegistrationService.NationalIdFormat))]
    [InlineData(1, "1000000014a", nameof(RegistrationService.NationalIdFormat))]
    public async Task Register_InvalidInput_IsRejectedWithoutTouchingARepository(int eventId, string nationalId, string expected)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync(Sessions.User, eventId, nationalId, Ct));

        Assert.Equal(Message(expected), error.Message);
        _registrations.VerifyNoOtherCalls();
        _events.VerifyNoOtherCalls();
    }

    // IDOR: a user cannot register somebody else's participant, or one of the original application.
    [Theory]
    [InlineData(ForeignNationalId)]
    [InlineData(LegacyNationalId)]
    [InlineData("10000000382")]
    public async Task Register_ParticipantNotOwnedOrUnknown_GetsTheSameAnswerAndWritesNothing(string nationalId)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync(Sessions.User, 1, nationalId, Ct));

        Assert.Equal(RegistrationService.ParticipantNotFound, error.Message);
        _registrations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_AdminMayRegisterAnyParticipant()
    {
        SetupAdd(3, 1, 40);

        var receipt = await Service().RegisterAsync(Sessions.Admin, 1, LegacyNationalId, Ct);

        Assert.Equal(40, receipt.RegistrationId);
    }

    [Theory]
    [InlineData(99, nameof(RegistrationService.EventRequired))]
    [InlineData(2, nameof(RegistrationService.EventFull))]
    [InlineData(3, nameof(RegistrationService.PastEvent))]
    public async Task Register_MissingFullOrPastEvent_IsRejectedBeforeWriting(int eventId, string expected)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync(Sessions.User, eventId, OwnNationalId, Ct));

        Assert.Equal(Message(expected), error.Message);
        _registrations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_OwnParticipantOnTheEventDay_IssuesARandomTicket()
    {
        SetupAdd(1, 1, 7);

        var first = await Service().RegisterAsync(Sessions.User, 1, $" {OwnNationalId} ", Ct);
        var second = await Service().RegisterAsync(Sessions.User, 1, OwnNationalId, Ct);

        Assert.Equal(new RegistrationReceipt(7, first.TicketCode, "Konser", Today), first);
        Assert.Matches("^ETK-[A-HJ-NP-Z2-9]{10}$", first.TicketCode);
        Assert.NotEqual(first.TicketCode, second.TicketCode);
        _registrations.Verify(r => r.AddAsync(1, 1, Today, Now.UtcDateTime, first.TicketCode, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_RulesRepeatedInsideTheTransaction_AreTranslated()
    {
        var service = Service();
        _registrations.SetupSequence(r => r.AddAsync(1, 1, Today, Now.UtcDateTime, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AlreadyRegisteredException())
            .ThrowsAsync(new EventFullException())
            .ThrowsAsync(new EventClosedException());

        Assert.Equal(RegistrationService.AlreadyRegistered, (await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegisterAsync(Sessions.User, 1, OwnNationalId, Ct))).Message);
        Assert.Equal(RegistrationService.EventFull, (await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegisterAsync(Sessions.User, 1, OwnNationalId, Ct))).Message);
        Assert.Equal(RegistrationService.PastEvent, (await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegisterAsync(Sessions.User, 1, OwnNationalId, Ct))).Message);
    }

    [Fact]
    public async Task List_UserSeesOwnAdminSeesAll()
    {
        _registrations.Setup(r => r.GetActiveAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Service().GetMyRegistrationsAsync(Sessions.User, Ct);
        await Service().GetMyRegistrationsAsync(Sessions.Admin, Ct);

        _registrations.Verify(r => r.GetActiveAsync(Sessions.User.UserId, It.IsAny<CancellationToken>()), Times.Once);
        _registrations.Verify(r => r.GetActiveAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // IDOR: somebody else's registration and a missing one get the same answer and nothing is written.
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(404)]
    public async Task Cancel_RegistrationNotOwned_IsDenied(int registrationId)
    {
        SetupOwnership(5, Sessions.OtherUser.UserId);
        SetupOwnership(6, null);
        _registrations.Setup(r => r.GetOwnershipAsync(404, It.IsAny<CancellationToken>())).ReturnsAsync((RegistrationOwnership?)null);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().CancelAsync(Sessions.User, registrationId, Ct));

        _registrations.Verify(
            r => r.CancelAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<DateOnly>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cancel_OwnRegistration_RepeatsTheOwnerInTheUpdate()
    {
        SetupOwnership(5, Sessions.User.UserId);
        _registrations.Setup(r => r.CancelAsync(5, Sessions.User.UserId, Today, Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Service().CancelAsync(Sessions.User, 5, Ct);

        _registrations.Verify(r => r.CancelAsync(5, Sessions.User.UserId, Today, Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_ByAdmin_HasNoOwnerFilter()
    {
        SetupOwnership(6, null);
        _registrations.Setup(r => r.CancelAsync(6, null, Today, Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Service().CancelAsync(Sessions.Admin, 6, Ct);
    }

    [Fact]
    public async Task Cancel_CancelledPastOrConcurrentlyChanged_IsRefused()
    {
        SetupOwnership(5, Sessions.User.UserId, status: RegistrationStatus.Cancelled);
        SetupOwnership(6, Sessions.User.UserId, eventDate: Today.AddDays(-1));
        SetupOwnership(7, Sessions.User.UserId);
        _registrations.Setup(r => r.CancelAsync(7, Sessions.User.UserId, Today, Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.Equal(RegistrationService.AlreadyCancelled, (await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CancelAsync(Sessions.User, 5, Ct))).Message);
        Assert.Equal(RegistrationService.PastCannotCancel, (await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CancelAsync(Sessions.User, 6, Ct))).Message);
        Assert.Equal(RegistrationService.CancelFailed, (await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CancelAsync(Sessions.User, 7, Ct))).Message);
    }

    private static string Message(string name) => name switch
    {
        nameof(RegistrationService.EventRequired) => RegistrationService.EventRequired,
        nameof(RegistrationService.NationalIdFormat) => RegistrationService.NationalIdFormat,
        nameof(RegistrationService.EventFull) => RegistrationService.EventFull,
        nameof(RegistrationService.PastEvent) => RegistrationService.PastEvent,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private void SetupAdd(int participantId, int eventId, int registrationId) =>
        _registrations.Setup(r => r.AddAsync(participantId, eventId, Today, Now.UtcDateTime, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(registrationId);

    private void SetupOwnership(int id, int? owner, RegistrationStatus status = RegistrationStatus.Active, DateOnly? eventDate = null) =>
        _registrations.Setup(r => r.GetOwnershipAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationOwnership(id, 1, eventDate ?? Today, owner, status));

    private RegistrationService Service() => new(_registrations.Object, _participants.Object, _events.Object, _time);
}
