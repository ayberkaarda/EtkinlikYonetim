using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Moq;

namespace EtkinlikYonetim.Business.Tests;

public sealed class ParticipantServiceTests
{
    private const string NationalId = "10000000146";

    private readonly Mock<IParticipantRepository> _participants = new(MockBehavior.Strict);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("", "Deniz Örnek", "deniz@example.com", nameof(ParticipantService.FieldsRequired))]
    [InlineData(NationalId, " ", "deniz@example.com", nameof(ParticipantService.FieldsRequired))]
    [InlineData(NationalId, "Deniz Örnek", "", nameof(ParticipantService.FieldsRequired))]
    [InlineData("10000000147", "Deniz Örnek", "deniz@example.com", nameof(ParticipantService.NationalIdInvalid))]
    [InlineData("1000000014", "Deniz Örnek", "deniz@example.com", nameof(ParticipantService.NationalIdInvalid))]
    [InlineData(NationalId, "Deniz 0rnek", "deniz@example.com", nameof(ParticipantService.FullNameInvalid))]
    [InlineData(NationalId, "Deniz  Örnek", "deniz@example.com", nameof(ParticipantService.FullNameInvalid))]
    [InlineData(NationalId, "Deniz Örnek", "deniz@example", nameof(ParticipantService.EmailInvalid))]
    [InlineData(NationalId, "Deniz Örnek", "deniz example@x.com", nameof(ParticipantService.EmailInvalid))]
    public async Task Add_InvalidInput_IsRejectedWithoutTouchingTheRepository(string nationalId, string fullName, string email, string expected)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.User, nationalId, fullName, email, Ct));

        Assert.Equal(Message(expected), error.Message);
        _participants.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Add_IsOwnedByTheSignedInAccount()
    {
        Participant? added = null;
        _participants.Setup(p => p.FindByNationalIdAsync(NationalId, Sessions.User.UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _participants.Setup(p => p.AddAsync(It.IsAny<Participant>(), It.IsAny<CancellationToken>()))
            .Callback<Participant, CancellationToken>((p, _) => added = p)
            .ReturnsAsync(11);

        var id = await Service().AddAsync(Sessions.User, $" {NationalId} ", " Deniz Örnek ", " deniz@example.com ", Ct);

        Assert.Equal(11, id);
        Assert.NotNull(added);
        Assert.Equal(Sessions.User.UserId, added.OwnerUserId);
        Assert.Equal(NationalId, added.NationalId);
        Assert.Equal("Deniz Örnek", added.FullName);
        Assert.Equal("deniz@example.com", added.Email);
    }

    [Fact]
    public async Task Add_ExistingOrConcurrentlyAddedNationalIdOfTheOwnList_IsRejected()
    {
        _participants.Setup(p => p.FindByNationalIdAsync(NationalId, Sessions.User.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ParticipantRecord(1, NationalId, "X", null, Sessions.User.UserId)]);
        _participants.Setup(p => p.FindByNationalIdAsync("10000000214", Sessions.User.UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _participants.Setup(p => p.AddAsync(It.IsAny<Participant>(), It.IsAny<CancellationToken>())).ThrowsAsync(new DuplicateNationalIdException());

        var existing = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.User, NationalId, "Deniz Örnek", "d@example.com", Ct));
        var raced = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.User, "10000000214", "Ece Taslak", "e@example.com", Ct));

        Assert.Equal(ParticipantService.NationalIdTaken, existing.Message);
        Assert.Equal(ParticipantService.NationalIdTaken, raced.Message);
    }

    /// <summary>
    /// Only the account's own list is consulted, so whether another account has the number cannot
    /// change the answer.
    /// </summary>
    [Fact]
    public async Task Add_LooksOnlyInTheOwnList()
    {
        _participants.Setup(p => p.FindByNationalIdAsync(NationalId, Sessions.User.UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _participants.Setup(p => p.AddAsync(It.IsAny<Participant>(), It.IsAny<CancellationToken>())).ReturnsAsync(12);

        Assert.Equal(12, await Service().AddAsync(Sessions.User, NationalId, "Deniz Örnek", "d@example.com", Ct));

        _participants.Verify(p => p.FindByNationalIdAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByEvent_ByAUser_IsDenied()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetByEventAsync(Sessions.User, 1, Ct));

        _participants.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetByEvent_MasksNationalIdsAndFillsMissingValues()
    {
        _participants.Setup(p => p.GetByEventAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new EventParticipant(1, "Deniz Örnek", NationalId, "deniz@example.com", "ETK-AAAAAAAAAA"),
            new EventParticipant(2, null, "20000000001", null, null),
        ]);

        var list = await Service().GetByEventAsync(Sessions.Admin, 1, Ct);

        Assert.Equal(new ParticipantView("Deniz Örnek", "100******46", "deniz@example.com", "ETK-AAAAAAAAAA"), list[0]);
        Assert.Equal(new ParticipantView("Ad Bilinmiyor", "200******01", "E-posta Yok", "Bilet Yok"), list[1]);
        Assert.All(list, p => Assert.DoesNotContain(p.MaskedNationalId, new[] { NationalId, "20000000001" }));
    }

    private static string Message(string name) => name switch
    {
        nameof(ParticipantService.FieldsRequired) => ParticipantService.FieldsRequired,
        nameof(ParticipantService.NationalIdInvalid) => ParticipantService.NationalIdInvalid,
        nameof(ParticipantService.FullNameInvalid) => ParticipantService.FullNameInvalid,
        nameof(ParticipantService.EmailInvalid) => ParticipantService.EmailInvalid,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private ParticipantService Service() => ServiceFactory.Participant(_participants.Object);
}
