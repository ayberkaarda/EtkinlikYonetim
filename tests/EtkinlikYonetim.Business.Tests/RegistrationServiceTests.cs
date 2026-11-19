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

    private readonly List<ParticipantRecord> _stored =
    [
        new(1, OwnNationalId, "Deniz Örnek", null, Sessions.User.UserId),
        new(2, ForeignNationalId, "Ece Taslak", null, Sessions.OtherUser.UserId),
        new(3, LegacyNationalId, "Eski Katılımcı", null, null),
    ];

    public RegistrationServiceTests()
    {
        // Behaves like the repository: filtered by owner when one is given, every account's otherwise.
        _participants.Setup(p => p.FindByNationalIdAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, int? owner, CancellationToken _) =>
                _stored.Where(r => r.NationalId == id && (owner is null || r.OwnerUserId == owner)).ToList());
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

    /// <summary>A number in the user's own list is found even when another account has it too.</summary>
    [Fact]
    public async Task Register_NationalIdOfSeveralAccounts_FindsTheUsersOwnParticipant()
    {
        _stored.Add(new ParticipantRecord(7, ForeignNationalId, "Kurgu Kişi", null, Sessions.User.UserId));
        SetupAdd(7, 1, 41);

        var receipt = await Service().RegisterAsync(Sessions.User, 1, ForeignNationalId, Ct);

        Assert.Equal(41, receipt.RegistrationId);
        _participants.Verify(p => p.FindByNationalIdAsync(ForeignNationalId, Sessions.User.UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Admins see every account's participants; a number that several accounts have is not guessed.</summary>
    [Fact]
    public async Task Register_AdminWithANationalIdOfSeveralAccounts_IsAskedToUseTheOwner()
    {
        _stored.Add(new ParticipantRecord(7, ForeignNationalId, "Kurgu Kişi", null, Sessions.User.UserId));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync(Sessions.Admin, 1, ForeignNationalId, Ct));

        Assert.Equal(RegistrationService.ParticipantAmbiguous, error.Message);
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
    [InlineData(3, nameof(RegistrationService.PastEvent))]
    public async Task Register_MissingOrPastEvent_IsRejectedBeforeWriting(int eventId, string expected)
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
        Assert.False(first.Waitlisted);
        Assert.Matches("^ETK-[A-HJ-NP-Z2-9]{26}$", first.TicketCode);
        Assert.NotEqual(first.TicketCode, second.TicketCode);
        _registrations.Verify(r => r.AddAsync(It.IsAny<Actor>(), 1, 1, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>A full event is no refusal: the repository puts the participant on the waiting list without a ticket.</summary>
    [Fact]
    public async Task Register_FullEvent_PutsTheParticipantOnTheWaitingList()
    {
        _registrations.Setup(r => r.AddAsync(It.IsAny<Actor>(), 1, 2, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()))
            .Returns((Actor _, int _, int _, Func<string> _, CancellationToken _) =>
                Task.FromResult(new RegistrationOutcome(12, RegistrationStatus.Waiting, 3)));

        var receipt = await Service().RegisterAsync(Sessions.User, 2, OwnNationalId, Ct);

        Assert.Equal(new RegistrationReceipt(12, null, "Dolu", Today.AddDays(3), 3), receipt);
        Assert.True(receipt.Waitlisted);
    }

    [Fact]
    public async Task Register_RulesRepeatedInsideTheTransaction_AreTranslated()
    {
        var service = Service();
        _registrations.SetupSequence(r => r.AddAsync(It.IsAny<Actor>(), 1, 1, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AlreadyRegisteredException())
            .ThrowsAsync(new EventClosedException());

        Assert.Equal(RegistrationService.AlreadyRegistered, (await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegisterAsync(Sessions.User, 1, OwnNationalId, Ct))).Message);
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

    /// <summary>An admin sees every registration but the ticket codes only of participants it added or that belong to no account.</summary>
    [Fact]
    public async Task List_AdminSeesOtherAccountsTicketCodesNot()
    {
        _registrations.Setup(r => r.GetActiveAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new RegistrationDetails(1, 1, "Konser", Today, 1, "Deniz", "ETK-OWN", OwnerUserId: Sessions.Admin.UserId),
            new RegistrationDetails(2, 1, "Konser", Today, 2, "Ece", "ETK-OTHER", OwnerUserId: Sessions.User.UserId),
            new RegistrationDetails(3, 1, "Konser", Today, 3, "Eski", "ETK-LEGACY", OwnerUserId: null),
        ]);

        var list = await Service().GetMyRegistrationsAsync(Sessions.Admin, Ct);

        Assert.Equal(["ETK-OWN", null, "ETK-LEGACY"], list.Select(r => r.TicketCode));
        Assert.Equal(3, list.Count);
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
            r => r.CancelAsync(It.IsAny<Actor>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cancel_OwnRegistration_RepeatsTheOwnerInTheUpdate()
    {
        SetupOwnership(5, Sessions.User.UserId);
        _registrations.Setup(r => r.CancelAsync(It.IsAny<Actor>(), 5, Sessions.User.UserId, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(CancellationOutcome.Cancelled);

        await Service().CancelAsync(Sessions.User, 5, Ct);

        _registrations.Verify(r => r.CancelAsync(It.IsAny<Actor>(), 5, Sessions.User.UserId, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_ByAdmin_HasNoOwnerFilter()
    {
        SetupOwnership(6, null);
        _registrations.Setup(r => r.CancelAsync(It.IsAny<Actor>(), 6, null, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(CancellationOutcome.CancelledAndPromoted);

        await Service().CancelAsync(Sessions.Admin, 6, Ct);
    }

    [Fact]
    public async Task Cancel_CancelledPastOrConcurrentlyChanged_IsRefused()
    {
        SetupOwnership(5, Sessions.User.UserId, status: RegistrationStatus.Cancelled);
        SetupOwnership(6, Sessions.User.UserId, eventDate: Today.AddDays(-1));
        SetupOwnership(7, Sessions.User.UserId);
        _registrations.Setup(r => r.CancelAsync(It.IsAny<Actor>(), 7, Sessions.User.UserId, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(CancellationOutcome.NotCancelled);

        Assert.Equal(RegistrationService.AlreadyCancelled, (await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CancelAsync(Sessions.User, 5, Ct))).Message);
        Assert.Equal(RegistrationService.PastCannotCancel, (await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CancelAsync(Sessions.User, 6, Ct))).Message);
        Assert.Equal(RegistrationService.CancelFailed, (await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CancelAsync(Sessions.User, 7, Ct))).Message);
    }

    /// <summary>A participant who has been let in keeps the seat: the cancellation is refused before any write.</summary>
    [Fact]
    public async Task Cancel_CheckedInRegistration_IsRefused()
    {
        _registrations.Setup(r => r.GetOwnershipAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationOwnership(5, 1, Today, Sessions.User.UserId, RegistrationStatus.Active, CheckedIn: true));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CancelAsync(Sessions.User, 5, Ct));

        Assert.Equal(RegistrationService.CheckedInCannotCancel, error.Message);
        _registrations.Verify(
            r => r.CancelAsync(It.IsAny<Actor>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>A participant can leave the waiting list like a seat.</summary>
    [Fact]
    public async Task Cancel_WaitingPlace_IsAllowed()
    {
        SetupOwnership(8, Sessions.User.UserId, status: RegistrationStatus.Waiting);
        _registrations.Setup(r => r.CancelAsync(It.IsAny<Actor>(), 8, Sessions.User.UserId, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(CancellationOutcome.Cancelled);

        await Service().CancelAsync(Sessions.User, 8, Ct);
    }

    /// <summary>
    /// The computer's clock is two days behind the database's. Yesterday by the database is still ahead
    /// by the computer's clock, but the database's day decides, and the registration is written with the
    /// database's day and time (the mock answers only to those).
    /// </summary>
    [Fact]
    public async Task Register_ComputerClockBehind_TheDatabaseDayAndTimeDecide()
    {
        var service = BehindByTwoDays();
        SetupAdd(1, 1, 41);

        var past = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegisterAsync(Sessions.User, 3, OwnNationalId, Ct));

        Assert.Equal(RegistrationService.PastEvent, past.Message);
        Assert.Equal(41, (await service.RegisterAsync(Sessions.User, 1, OwnNationalId, Ct)).RegistrationId);
    }

    /// <summary>A registration of yesterday's event (by the database) cannot be cancelled from a computer whose clock is behind.</summary>
    [Fact]
    public async Task Cancel_ComputerClockBehind_TheDatabaseDayAndTimeDecide()
    {
        var service = BehindByTwoDays();
        SetupOwnership(6, Sessions.User.UserId, eventDate: Today.AddDays(-1));
        SetupOwnership(7, Sessions.User.UserId);
        _registrations.Setup(r => r.CancelAsync(It.IsAny<Actor>(), 7, Sessions.User.UserId, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(CancellationOutcome.Cancelled);

        Assert.Equal(RegistrationService.PastCannotCancel, (await Assert.ThrowsAsync<BusinessRuleException>(() => service.CancelAsync(Sessions.User, 6, Ct))).Message);
        await service.CancelAsync(Sessions.User, 7, Ct);
    }

    private RegistrationService BehindByTwoDays()
    {
        var behind = new FakeTimeProvider(Now - TimeSpan.FromDays(2));
        return ServiceFactory.Registration(_registrations.Object, _participants.Object, _events.Object, behind, clock: new TestDatabaseClock(behind, TimeSpan.FromDays(2)));
    }

    private static string Message(string name) => name switch
    {
        nameof(RegistrationService.EventRequired) => RegistrationService.EventRequired,
        nameof(RegistrationService.NationalIdFormat) => RegistrationService.NationalIdFormat,
        nameof(RegistrationService.PastEvent) => RegistrationService.PastEvent,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private void SetupAdd(int participantId, int eventId, int registrationId) =>
        _registrations.Setup(r => r.AddAsync(It.IsAny<Actor>(), participantId, eventId, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()))
            .Returns((Actor _, int _, int _, Func<string> newTicketCode, CancellationToken _) =>
            {
                newTicketCode();
                return Task.FromResult(new RegistrationOutcome(registrationId, RegistrationStatus.Active, null));
            });

    private void SetupOwnership(int id, int? owner, RegistrationStatus status = RegistrationStatus.Active, DateOnly? eventDate = null) =>
        _registrations.Setup(r => r.GetOwnershipAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationOwnership(id, 1, eventDate ?? Today, owner, status));

    private RegistrationService Service() => ServiceFactory.Registration(_registrations.Object, _participants.Object, _events.Object, _time);
}
