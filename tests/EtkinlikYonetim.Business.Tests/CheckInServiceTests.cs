using System.Security.Cryptography;
using System.Text;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>Ticket codes and the entrance, with strict mocks.</summary>
public sealed class CheckInServiceTests
{
    private const string Code = "ETK-ABCDEFGHJKLMNPQRSTUVWXYZ";
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 18, 0, 0, TimeSpan.Zero);
    private const string ParticipantName = "Deniz Örnek";
    private static readonly DateOnly Today = new(2026, 3, 2);
    private static readonly CheckInCount Count = new(4, 10);

    private readonly Mock<ITicketRepository> _tickets = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingAudit _audit = new();

    public CheckInServiceTests() =>
        _tickets.Setup(t => t.GetCheckInCountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Count);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void NewCodes_HaveTheFormatAnd130RandomBits()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => TicketCode.New()).ToList();

        Assert.All(codes, c => Assert.Matches("^ETK-[A-HJ-NP-Z2-9]{26}$", c));
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        // 26 characters of a 32 letter alphabet: 5 bits each.
        Assert.True(TicketCode.RandomLength * Math.Log2(32) >= 128);
        // Every letter of the alphabet turns up, so no position is fixed.
        Assert.Equal(32, codes.SelectMany(c => c[TicketCode.Prefix.Length..]).Distinct().Count());
    }

    [Theory]
    [InlineData(" etk-abcdefghjklmnpqrstuvwxyz ", true, Code)]
    [InlineData("ETK-0123456789ABCDEF0123456789ABCDEF", true, "ETK-0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("", false, "")]
    [InlineData("ETK-", false, "ETK-")]
    [InlineData("XYZ-ABCDEFGHJK", false, "XYZ-ABCDEFGHJK")]
    [InlineData("ETK-ABC DEF", false, "ETK-ABC DEF")]
    [InlineData("ETK-ABC'--", false, "ETK-ABC'--")]
    [InlineData("ETK-ÇĞÜŞ", false, "ETK-ÇĞÜŞ")]
    public void Normalize_AcceptsOnlyTheShapeOfACode(string typed, bool valid, string normalized)
    {
        Assert.Equal(valid, TicketCode.TryNormalize(typed, out var code));
        Assert.Equal(normalized, code);
    }

    [Fact]
    public void Normalize_RefusesCodesLongerThanTheColumn() =>
        Assert.False(TicketCode.TryNormalize("ETK-" + new string('A', 37), out _));

    [Fact]
    public void Hash_IsTheSha256OfTheAsciiText() =>
        Assert.Equal(SHA256.HashData(Encoding.ASCII.GetBytes(Code)), TicketCodeHash.Of(Code));

    [Fact]
    public async Task CheckIn_ByAUser_IsDenied()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().CheckInAsync(Sessions.User, 1, Code, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetCountAsync(Sessions.User, 1, Ct));

        _tickets.VerifyNoOtherCalls();
    }

    /// <summary>A valid, unused ticket of the event is checked in; a typed code in lower case matches.</summary>
    [Fact]
    public async Task CheckIn_ValidUnusedTicket_IsCheckedInByItsHash()
    {
        Found(Ticket(9, Code, 1, RegistrationStatus.Active, false, null));
        _tickets.Setup(t => t.CheckInAsync(It.IsAny<Actor>(), 9, 1, Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Service().CheckInAsync(Sessions.Admin, 1, Code.ToLowerInvariant(), Ct);

        Assert.Equal(new CheckInResult(CheckInOutcome.CheckedIn, Count, ParticipantName), result);
        _tickets.Verify(t => t.FindForCheckInAsync(It.Is<byte[]>(h => h.SequenceEqual(TicketCodeHash.Of(Code))), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal([(AuditEvent.TicketCheckedIn, (UserRole?)UserRole.Admin)], _audit.Events);
    }

    [Fact]
    public async Task CheckIn_UsedTicket_IsAlreadyUsedWithoutAnUpdate()
    {
        Found(Ticket(9, Code, 1, RegistrationStatus.Active, false, Now.UtcDateTime.AddMinutes(-5)));

        var result = await Service().CheckInAsync(Sessions.Admin, 1, Code, Ct);

        Assert.Equal(CheckInOutcome.AlreadyUsed, result.Outcome);
        _tickets.Verify(t => t.CheckInAsync(It.IsAny<Actor>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The UPDATE lost to a concurrent scan of the same ticket: the second scan reads it again and says "already used".</summary>
    [Fact]
    public async Task CheckIn_LostToAConcurrentScan_IsAlreadyUsed()
    {
        _tickets.SetupSequence(t => t.FindForCheckInAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ticket(9, Code, 1, RegistrationStatus.Active, false, null))
            .ReturnsAsync(Ticket(9, Code, 1, RegistrationStatus.Active, false, Now.UtcDateTime));
        _tickets.Setup(t => t.CheckInAsync(It.IsAny<Actor>(), 9, 1, Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.Equal(CheckInOutcome.AlreadyUsed, (await Service().CheckInAsync(Sessions.Admin, 1, Code, Ct)).Outcome);
    }

    /// <summary>
    /// Every refusal looks the same: an unknown code, a ticket of another event, of a cancelled
    /// registration, a waiting place, a cancelled event, and a code whose stored text differs although its
    /// hash was found (the constant time comparison of the code itself decides, not the lookup).
    /// </summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("other event")]
    [InlineData("cancelled registration")]
    [InlineData("closed with the event")]
    [InlineData("cancelled event")]
    [InlineData("stored code differs")]
    public async Task CheckIn_AnythingButAValidTicketOfTheEvent_IsRejected(string variant)
    {
        Found(variant switch
        {
            "unknown" => null,
            "other event" => Ticket(9, Code, 2, RegistrationStatus.Active, false, null),
            "cancelled registration" => Ticket(9, Code, 1, RegistrationStatus.Cancelled, false, null),
            "closed with the event" => Ticket(9, Code, 1, RegistrationStatus.EventCancelled, true, null),
            "cancelled event" => Ticket(9, Code, 1, RegistrationStatus.Active, true, null),
            _ => Ticket(9, "ETK-ABCDEFGHJKLMNPQRSTUVWXY2", 1, RegistrationStatus.Active, false, null),
        });

        var result = await Service().CheckInAsync(Sessions.Admin, 1, Code, Ct);

        Assert.Equal(new CheckInResult(CheckInOutcome.Rejected, Count), result);
        _tickets.Verify(t => t.CheckInAsync(It.IsAny<Actor>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal([(AuditEvent.TicketRejected, (UserRole?)UserRole.Admin)], _audit.Events);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1' OR '1'='1")]
    [InlineData("ETK-")]
    public async Task CheckIn_TextThatIsNoCode_IsRejectedWithoutALookup(string typed)
    {
        var result = await Service().CheckInAsync(Sessions.Admin, 1, typed, Ct);

        Assert.Equal(CheckInOutcome.Rejected, result.Outcome);
        _tickets.Verify(t => t.FindForCheckInAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A valid ticket of the event on another day than today (by the database's clock) is neither used nor
    /// refused as invalid: nothing is written, so the ticket still works on the event's day.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task CheckIn_ValidTicketOnAnotherDay_IsNotUsed(int days)
    {
        Found(Ticket(9, Code, 1, RegistrationStatus.Active, false, null) with { EventDate = Today.AddDays(days) });

        var result = await Service().CheckInAsync(Sessions.Admin, 1, Code, Ct);

        Assert.Equal(new CheckInResult(CheckInOutcome.NotEventDay, Count), result);
        _tickets.Verify(t => t.CheckInAsync(It.IsAny<Actor>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A valid ticket of the event, today, with the participant's name for the staff.</summary>
    private static TicketForCheckIn Ticket(int id, string code, int eventId, RegistrationStatus status, bool eventCancelled, DateTime? checkedInAt) =>
        new(id, code, eventId, status, eventCancelled, checkedInAt, Today, ParticipantName);

    private void Found(TicketForCheckIn? ticket) =>
        _tickets.Setup(t => t.FindForCheckInAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

    private CheckInService Service() => ServiceFactory.CheckIn(_tickets.Object, _time, _audit);
}
