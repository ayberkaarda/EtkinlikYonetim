using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// Services and repositories together on SQLite in-memory: sign-in, lockout, password migration,
/// events, participants, registrations with tickets, the seat counter and the ownership rules end to
/// end. Each test gets a fresh database.
/// </summary>
public sealed class SqliteServiceTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IAuthService Auth => _db.Get<IAuthService>();

    private IEventService Events => _db.Get<IEventService>();

    private IParticipantService Participants => _db.Get<IParticipantService>();

    private IRegistrationService Registrations => _db.Get<IRegistrationService>();

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Seed_ContainsOnlyFictionalAccountsThatCanSignIn()
    {
        var admin = await Auth.SignInAsync("admin", "Demo.Admin1", Ct);
        var user = await Auth.SignInAsync("ornek", "Demo.Kullanici1", Ct);

        Assert.True(admin.IsAdmin);
        Assert.Equal(UserRole.User, user.Role);
        Assert.Equal(["Bahar Konseri", "Yazılım Atölyesi", "Kısa Film Gösterimi"], (await Events.GetEventsAsync(user, Ct)).Select(e => e.Name));

        await using var context = _db.CreateContext();
        Assert.All(await context.Participants.Select(p => p.NationalId).ToListAsync(Ct), id => Assert.True(NationalIdNumber.IsValid(id)));
        Assert.All(await context.Participants.Select(p => p.Email).ToListAsync(Ct), e => Assert.EndsWith("@example.com", e, StringComparison.Ordinal));
        Assert.All(await context.Users.Select(u => u.PasswordHash).ToListAsync(Ct), h => Assert.StartsWith("PBKDF2-SHA256$600000$", h, StringComparison.Ordinal));
        Assert.Empty(await context.Registrations.ToListAsync(Ct));
    }

    [Fact]
    public async Task Register_StoresAHashAndAllowsSignIn()
    {
        await Auth.RegisterAsync("yeniuye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);

        var session = await Auth.SignInAsync("yeniuye", "Gizli.Sifre1", Ct);
        await using var context = _db.CreateContext();
        var stored = await context.Users.SingleAsync(u => u.Id == session.UserId, Ct);

        Assert.Equal(UserRole.User, session.Role);
        Assert.StartsWith("PBKDF2-SHA256$", stored.PasswordHash, StringComparison.Ordinal);
        Assert.DoesNotContain("Gizli.Sifre1", stored.PasswordHash, StringComparison.Ordinal);
        var duplicate = await Assert.ThrowsAsync<BusinessRuleException>(() => Auth.RegisterAsync("yeniuye", "Gizli.Sifre2", "Gizli.Sifre2", Ct));
        Assert.Equal("Bu kullanıcı adı zaten kullanılıyor.", duplicate.Message);
    }

    [Fact]
    public async Task Lockout_AfterFiveFailures_RefusesTheRightPasswordUntilTheLockEnds()
    {
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => Auth.SignInAsync("ornek", "yanlis", Ct));
        }

        var locked = await Assert.ThrowsAsync<BusinessRuleException>(() => Auth.SignInAsync("ornek", "Demo.Kullanici1", Ct));
        Assert.Equal(AuthService.CredentialsInvalid, locked.Message);

        _db.Clock.Advance(TimeSpan.FromMinutes(14));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Auth.SignInAsync("ornek", "Demo.Kullanici1", Ct));

        _db.Clock.Advance(TimeSpan.FromMinutes(1));
        var session = await Auth.SignInAsync("ornek", "Demo.Kullanici1", Ct);

        await using var context = _db.CreateContext();
        var user = await context.Users.SingleAsync(u => u.Id == session.UserId, Ct);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutEnd);
    }

    [Fact]
    public async Task Lockout_SuccessfulSignInResetsTheCounter()
    {
        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => Auth.SignInAsync("admin", "yanlis", Ct));
        }

        await Auth.SignInAsync("admin", "Demo.Admin1", Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Auth.SignInAsync("admin", "yanlis", Ct));

        // One failure after the reset does not lock the account.
        await Auth.SignInAsync("admin", "Demo.Admin1", Ct);
    }

    [Fact]
    public async Task LegacyPlainTextPassword_IsHashedOnTheFirstSuccessfulSignIn()
    {
        // As written by the original application: the password in plain text.
        await using (var context = _db.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE Kullanicilar SET Sifre = 'eski1', SifreBicimi = 1 WHERE KullaniciAdi = 'ornek'", Ct);
        }

        await Assert.ThrowsAsync<BusinessRuleException>(() => Auth.SignInAsync("ornek", "eski2", Ct));
        await Auth.SignInAsync("ornek", " eski1 ", Ct); // the original application trimmed passwords

        await using (var context = _db.CreateContext())
        {
            var stored = await context.Users.SingleAsync(u => u.UserName == "ornek", Ct);
            Assert.StartsWith("PBKDF2-SHA256$1000$", stored.PasswordHash, StringComparison.Ordinal);
            Assert.Equal(PasswordFormat.Pbkdf2, stored.PasswordFormat);
        }

        await Auth.SignInAsync("ornek", "eski1", Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Auth.SignInAsync("ornek", "eski2", Ct));
    }

    [Fact]
    public async Task CreateEvent_AdminOnly_AndStoredWithAnEmptySeatCounter()
    {
        var id = await Events.CreateAsync(Admin(SeedAdminId), " Bahar Şenliği ", new DateTime(2026, 3, 2), 5, Ct);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Events.CreateAsync(User(SeedUserId), "Gizli Etkinlik", new DateTime(2026, 4, 1), 5, Ct));
        var created = (await Events.GetEventsAsync(User(SeedUserId), Ct)).Single(e => e.Id == id);
        Assert.Equal("Bahar Şenliği", created.Name);
        Assert.Equal(new DateOnly(2026, 3, 2), created.Date);
        Assert.Equal(0, created.RegisteredCount);
        Assert.DoesNotContain(await Events.GetEventsAsync(User(SeedUserId), Ct), e => e.Name == "Gizli Etkinlik");
    }

    [Fact]
    public async Task OpenEvents_LeaveOutPastEvents()
    {
        await Events.CreateAsync(Admin(SeedAdminId), "Bugün", new DateTime(2026, 3, 2), 5, Ct);
        _db.Clock.Advance(TimeSpan.FromDays(1));

        var open = await Events.GetOpenEventsAsync(User(SeedUserId), Ct);

        Assert.DoesNotContain(open, e => e.Name == "Bugün");
        Assert.Equal(3, open.Count);
    }

    [Fact]
    public async Task Register_IssuesATicketAndTakesASeat()
    {
        var receipt = await Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct);

        Assert.Matches("^ETK-[A-HJ-NP-Z2-9]{26}$", receipt.TicketCode);
        Assert.Equal("Bahar Konseri", receipt.EventName);
        var mine = await Registrations.GetMyRegistrationsAsync(User(SeedUserId), Ct);
        Assert.Equal(receipt.TicketCode, Assert.Single(mine).TicketCode);
        Assert.Equal(1, (await Events.GetEventsAsync(User(SeedUserId), Ct)).Single(e => e.Id == ConcertId).RegisteredCount);

        await using var context = _db.CreateContext();
        var ticket = await context.Tickets.SingleAsync(Ct);
        Assert.Equal(receipt.RegistrationId, ticket.RegistrationId);
        Assert.Equal(Start.UtcDateTime, ticket.IssuedAt);
    }

    [Fact]
    public async Task Register_SameParticipantTwice_IsRejectedAndKeepsOneSeat()
    {
        await Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct));

        Assert.Equal("Bu katılımcı bu etkinliğe zaten kayıtlı veya bekleme listesinde.", error.Message);
        Assert.Equal(1, (await Events.GetEventsAsync(User(SeedUserId), Ct)).Single(e => e.Id == ConcertId).RegisteredCount);
    }

    [Fact]
    public async Task Repository_SecondRegistrationOfTheSameParticipant_IsReportedAndTheSeatIsKept()
    {
        var repository = _db.Get<IRegistrationRepository>();
        await repository.AddAsync(Access.ActorOf(Admin(SeedAdminId)), 1, ConcertId, () => "ETK-AAAAAAAAAA", Ct);

        await Assert.ThrowsAsync<AlreadyRegisteredException>(() => repository.AddAsync(Access.ActorOf(Admin(SeedAdminId)), 1, ConcertId, () => "ETK-BBBBBBBBBB", Ct));

        await using var context = _db.CreateContext();
        Assert.Equal(1, (await context.Events.SingleAsync(e => e.Id == ConcertId, Ct)).RegisteredCount);
    }

    [Fact]
    public async Task Database_FilteredUniqueIndex_RejectsASecondActiveRegistrationButNotACancelledOne()
    {
        await using var context = _db.CreateContext();
        context.Registrations.Add(new Registration { ParticipantId = 1, EventId = ConcertId, CreatedAt = Start.UtcDateTime });
        context.Registrations.Add(new Registration { ParticipantId = 1, EventId = ConcertId, CreatedAt = Start.UtcDateTime, Status = RegistrationStatus.Cancelled });
        await context.SaveChangesAsync(Ct);

        // Inserted directly, past every check in the services and repositories.
        context.Registrations.Add(new Registration { ParticipantId = 1, EventId = ConcertId, CreatedAt = Start.UtcDateTime });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }

    /// <summary>Nobody waits twice: a waiting place counts like a seat in the unique index, and queue numbers are unique per event.</summary>
    [Fact]
    public async Task Database_FilteredUniqueIndexes_RejectASecondWaitingPlaceAndARepeatedQueueNumber()
    {
        await using (var context = _db.CreateContext())
        {
            context.Registrations.Add(new Registration { ParticipantId = 1, EventId = ConcertId, CreatedAt = Start.UtcDateTime, Status = RegistrationStatus.Waiting, QueueNumber = 1 });
            await context.SaveChangesAsync(Ct);
        }

        await using (var context = _db.CreateContext())
        {
            context.Registrations.Add(new Registration { ParticipantId = 1, EventId = ConcertId, CreatedAt = Start.UtcDateTime, Status = RegistrationStatus.Waiting, QueueNumber = 2 });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
        }

        await using (var context = _db.CreateContext())
        {
            context.Registrations.Add(new Registration { ParticipantId = 2, EventId = ConcertId, CreatedAt = Start.UtcDateTime, Status = RegistrationStatus.Waiting, QueueNumber = 1 });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task Database_CheckConstraint_RejectsMoreRegistrationsThanSeats()
    {
        await using var context = _db.CreateContext();

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => context.Database.ExecuteSqlRawAsync("UPDATE Etkinlikler SET KayitSayisi = Kapasite + 1 WHERE Id = 3", Ct));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => context.Database.ExecuteSqlRawAsync("UPDATE Etkinlikler SET KayitSayisi = -1 WHERE Id = 3", Ct));
    }

    [Fact]
    public async Task Register_FullEvent_JoinsTheWaitingListWithoutATicket()
    {
        var admin = Admin(SeedAdminId);
        await Registrations.RegisterAsync(admin, SmallEventId, DenizNationalId, Ct);
        await Registrations.RegisterAsync(admin, SmallEventId, EceNationalId, Ct);

        var waiting = await Registrations.RegisterAsync(admin, SmallEventId, MertNationalId, Ct);

        Assert.True(waiting.Waitlisted);
        Assert.Equal((null, 1), (waiting.TicketCode, waiting.QueuePosition));
        var summary = (await Events.GetEventsAsync(admin, Ct)).Single(e => e.Id == SmallEventId);
        Assert.Equal((2, 1), (summary.RegisteredCount, summary.WaitingCount));
        var mine = await Registrations.GetMyRegistrationsAsync(admin, Ct);
        Assert.Equal((RegistrationStatus.Waiting, 1, null), mine.Where(r => r.Id == waiting.RegistrationId).Select(r => (r.Status, r.QueuePosition, r.TicketCode)).Single());
    }

    /// <summary>The same participant cannot wait twice, nor wait while holding a seat.</summary>
    [Fact]
    public async Task Register_WaitingParticipantAgain_IsRejected()
    {
        var admin = Admin(SeedAdminId);
        await Registrations.RegisterAsync(admin, SmallEventId, DenizNationalId, Ct);
        await Registrations.RegisterAsync(admin, SmallEventId, EceNationalId, Ct);
        await Registrations.RegisterAsync(admin, SmallEventId, MertNationalId, Ct);

        var waitingAgain = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(admin, SmallEventId, MertNationalId, Ct));
        var seatedAgain = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(admin, SmallEventId, DenizNationalId, Ct));

        Assert.Equal(RegistrationService.AlreadyRegistered, waitingAgain.Message);
        Assert.Equal(RegistrationService.AlreadyRegistered, seatedAgain.Message);
        await using var context = _db.CreateContext();
        Assert.Equal(3, await context.Registrations.CountAsync(r => r.EventId == SmallEventId, Ct));
    }

    /// <summary>
    /// Queue numbers follow the order of arrival; a cancelled seat goes to number 1, a waiting place that
    /// is given up moves the others up, and the counter always equals the seats held.
    /// </summary>
    [Fact]
    public async Task WaitingList_MovesUpInOrderOnCancellations()
    {
        var admin = Admin(SeedAdminId);
        var participants = _db.Get<IParticipantService>();
        string[] extra = ["10000000528", "10000000696"];
        foreach (var nationalId in extra)
        {
            await participants.AddAsync(admin, nationalId, "Sıra Kişisi", "sira@example.com", Ct);
        }

        var deniz = await Registrations.RegisterAsync(admin, SmallEventId, DenizNationalId, Ct);
        await Registrations.RegisterAsync(admin, SmallEventId, EceNationalId, Ct);
        var mert = await Registrations.RegisterAsync(admin, SmallEventId, MertNationalId, Ct);
        var fourth = await Registrations.RegisterAsync(admin, SmallEventId, extra[0], Ct);
        var fifth = await Registrations.RegisterAsync(admin, SmallEventId, extra[1], Ct);
        Assert.Equal([1, 2, 3], new[] { mert, fourth, fifth }.Select(r => r.QueuePosition!.Value));

        await Registrations.CancelAsync(admin, fourth.RegistrationId, Ct); // leaves the waiting list
        await Registrations.CancelAsync(admin, deniz.RegistrationId, Ct); // frees a seat for number 1

        var mine = (await Registrations.GetMyRegistrationsAsync(admin, Ct)).Where(r => r.EventId == SmallEventId).ToDictionary(r => r.Id);
        Assert.Equal(RegistrationStatus.Active, mine[mert.RegistrationId].Status);
        Assert.NotNull(mine[mert.RegistrationId].TicketCode);
        Assert.Equal((RegistrationStatus.Waiting, 1), (mine[fifth.RegistrationId].Status, mine[fifth.RegistrationId].QueuePosition!.Value));
        Assert.False(mine.ContainsKey(deniz.RegistrationId));
        Assert.False(mine.ContainsKey(fourth.RegistrationId));
        await using var context = _db.CreateContext();
        Assert.Equal(2, (await context.Events.SingleAsync(e => e.Id == SmallEventId, Ct)).RegisteredCount);
        Assert.Equal(3, await context.Tickets.CountAsync(t => context.Registrations.Any(r => r.Id == t.RegistrationId && r.EventId == SmallEventId), Ct));
    }

    [Fact]
    public async Task Repository_FullOrPastEvent_IsDecidedInsideTheTransaction()
    {
        var repository = _db.Get<IRegistrationRepository>();
        var codes = new Queue<string>(["ETK-AAAAAAAAAA", "ETK-BBBBBBBBBB", "ETK-CCCCCCCCCC"]);
        await repository.AddAsync(Access.ActorOf(Admin(SeedAdminId)), 1, SmallEventId, codes.Dequeue, Ct);
        await repository.AddAsync(Access.ActorOf(Admin(SeedAdminId)), 2, SmallEventId, codes.Dequeue, Ct);

        var waiting = await repository.AddAsync(Access.ActorOf(Admin(SeedAdminId)), 3, SmallEventId, codes.Dequeue, Ct);

        // The day is the database's at the write: past the concert's day, the concert is closed.
        _db.Clock.MoveTo(new DateOnly(2027, 4, 18));
        await Assert.ThrowsAsync<EventClosedException>(() => repository.AddAsync(Access.ActorOf(Admin(SeedAdminId)), 3, ConcertId, codes.Dequeue, Ct));
        await Assert.ThrowsAsync<EventClosedException>(() => repository.AddAsync(Access.ActorOf(Admin(SeedAdminId)), 3, 999, codes.Dequeue, Ct));

        Assert.Equal((RegistrationStatus.Waiting, 1), (waiting.Status, waiting.QueuePosition!.Value));
        Assert.Single(codes); // only the two seats drew a ticket code
        await using var context = _db.CreateContext();
        Assert.Equal(3, await context.Registrations.CountAsync(Ct));
        Assert.Equal(2, await context.Tickets.CountAsync(Ct));
    }

    [Fact]
    public async Task Register_PastEvent_IsRejected()
    {
        var id = await Events.CreateAsync(Admin(SeedAdminId), "Bugünkü Etkinlik", new DateTime(2026, 3, 2), 5, Ct);
        await Registrations.RegisterAsync(User(SeedUserId), id, DenizNationalId, Ct); // the day itself is allowed
        _db.Clock.Advance(TimeSpan.FromDays(1));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(User(SeedUserId), id, EceNationalId, Ct));

        Assert.Equal("Geçmiş tarihli bir etkinliğe kayıt yapılamaz.", error.Message);
    }

    [Fact]
    public async Task AddParticipant_IsOwnedByTheSignedInAccount()
    {
        var user = User(SeedUserId);
        var id = await Participants.AddAsync(user, NewNationalId, "Yeni Katılımcı", "yeni@example.com", Ct);

        await using (var context = _db.CreateContext())
        {
            Assert.Equal(SeedUserId, (await context.Participants.SingleAsync(p => p.Id == id, Ct)).OwnerUserId);
        }

        var duplicate = await Assert.ThrowsAsync<BusinessRuleException>(() => Participants.AddAsync(user, NewNationalId, "Başka Biri", "baska@example.com", Ct));
        Assert.Equal("Bu T.C. Kimlik No ile listenizde kayıtlı bir katılımcı zaten var.", duplicate.Message);
        await Registrations.RegisterAsync(user, ConcertId, NewNationalId, Ct);
    }

    /// <summary>
    /// National ids are unique per owning account, so adding a participant answers the same whether or
    /// not another account already has one with that number; nothing about the other account's
    /// participant is revealed or changed.
    /// </summary>
    [Fact]
    public async Task AddParticipant_ForeignNationalId_DoesNotDiscloseExistence()
    {
        var otherUser = await RegisterAndSignInAsync("baskauye");

        var foreignId = await Participants.AddAsync(otherUser, DenizNationalId, "Kurgu Kişi", "kurgu@example.com", Ct);
        var newId = await Participants.AddAsync(otherUser, NewNationalId, "Kurgu Kişi", "kurgu@example.com", Ct);

        Assert.True(foreignId > 0 && newId > 0);
        await using (var context = _db.CreateContext())
        {
            var deniz = await context.Participants.AsNoTracking().SingleAsync(p => p.NationalId == DenizNationalId && p.OwnerUserId == SeedUserId, Ct);
            Assert.Equal("Deniz Örnek", deniz.FullName);
            Assert.Equal(2, await context.Participants.CountAsync(p => p.NationalId == DenizNationalId, Ct));
            Assert.Equal(otherUser.UserId, (await context.Participants.SingleAsync(p => p.Id == foreignId, Ct)).OwnerUserId);
        }

        // The repeated id in the account's own list is still refused, with the same answer for both numbers.
        var ownForeign = await Assert.ThrowsAsync<BusinessRuleException>(() => Participants.AddAsync(otherUser, DenizNationalId, "Kurgu Kişi", "kurgu@example.com", Ct));
        var ownNew = await Assert.ThrowsAsync<BusinessRuleException>(() => Participants.AddAsync(otherUser, NewNationalId, "Kurgu Kişi", "kurgu@example.com", Ct));
        Assert.Equal(ownNew.Message, ownForeign.Message);

        // Registration by national id finds each account's own participant.
        var mine = await Registrations.RegisterAsync(otherUser, ConcertId, DenizNationalId, Ct);
        var theirs = await Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct);
        Assert.NotEqual(mine.RegistrationId, theirs.RegistrationId);
        Assert.Equal(["Kurgu Kişi"], (await Registrations.GetMyRegistrationsAsync(otherUser, Ct)).Select(r => r.ParticipantName));
        Assert.Equal(["Deniz Örnek"], (await Registrations.GetMyRegistrationsAsync(User(SeedUserId), Ct)).Select(r => r.ParticipantName));
    }

    /// <summary>
    /// The unique index path (an insert that raced the check) reports a duplicate only for the
    /// account's own list; the same number under another account is no conflict.
    /// </summary>
    [Fact]
    public async Task Repository_DuplicateNationalId_IsReportedOnlyForTheSameOwner()
    {
        var repository = _db.Get<IParticipantRepository>();

        var foreign = await repository.AddAsync(Access.ActorOf(Admin(SeedAdminId)), new Participant { NationalId = DenizNationalId, FullName = "Kurgu Kişi", OwnerUserId = SeedAdminId }, Ct);
        await Assert.ThrowsAsync<DuplicateNationalIdException>(
            () => repository.AddAsync(Access.ActorOf(Admin(SeedAdminId)), new Participant { NationalId = DenizNationalId, FullName = "Kurgu Kişi", OwnerUserId = SeedUserId }, Ct));

        Assert.True(foreign > 0);
    }

    // IDOR: valid sessions must not reach other accounts' participants and registrations by passing their ids.
    [Fact]
    public async Task Register_SomebodyElsesParticipant_LooksLikeAnUnknownOne()
    {
        var otherUser = await RegisterAndSignInAsync("baskauye");

        var foreign = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(otherUser, ConcertId, DenizNationalId, Ct));
        var unknown = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(otherUser, ConcertId, NewNationalId, Ct));
        var adminOwned = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(User(SeedUserId), ConcertId, MertNationalId, Ct));

        Assert.Equal(unknown.Message, foreign.Message);
        Assert.Equal(unknown.Message, adminOwned.Message);
        await using var context = _db.CreateContext();
        Assert.Equal(0, await context.Registrations.CountAsync(Ct));
    }

    [Fact]
    public async Task Lists_ShowOnlyTheRegistrationsOfTheAccountsOwnParticipants()
    {
        var otherUser = await RegisterAndSignInAsync("baskauye");
        await Participants.AddAsync(otherUser, NewNationalId, "Başka Katılımcı", "baska@example.com", Ct);
        await Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct);
        await Registrations.RegisterAsync(otherUser, ConcertId, NewNationalId, Ct);
        await Registrations.RegisterAsync(Admin(SeedAdminId), ConcertId, MertNationalId, Ct);

        var mine = await Registrations.GetMyRegistrationsAsync(User(SeedUserId), Ct);
        var theirs = await Registrations.GetMyRegistrationsAsync(otherUser, Ct);
        var all = await Registrations.GetMyRegistrationsAsync(Admin(SeedAdminId), Ct);

        Assert.Equal(["Deniz Örnek"], mine.Select(r => r.ParticipantName));
        Assert.Equal(["Başka Katılımcı"], theirs.Select(r => r.ParticipantName));
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task Cancel_OtherAccountsRegistration_IsDeniedAndTheRowStaysActive()
    {
        var otherUser = await RegisterAndSignInAsync("baskauye");
        var receipt = await Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct);

        var denied = await Assert.ThrowsAsync<AccessDeniedException>(() => Registrations.CancelAsync(otherUser, receipt.RegistrationId, Ct));
        var missing = await Assert.ThrowsAsync<AccessDeniedException>(() => Registrations.CancelAsync(otherUser, 9999, Ct));

        Assert.Equal(missing.Message, denied.Message);
        await using (var context = _db.CreateContext())
        {
            Assert.Equal(RegistrationStatus.Active, (await context.Registrations.SingleAsync(Ct)).Status);
        }

        // Admins may cancel any registration.
        await Registrations.CancelAsync(Admin(SeedAdminId), receipt.RegistrationId, Ct);
        Assert.Empty(await Registrations.GetMyRegistrationsAsync(User(SeedUserId), Ct));
    }

    [Fact]
    public async Task Cancel_FreesTheSeatKeepsTheRowAndAllowsANewRegistration()
    {
        var user = User(SeedUserId);
        var receipt = await Registrations.RegisterAsync(user, SmallEventId, DenizNationalId, Ct);
        await Registrations.RegisterAsync(user, SmallEventId, EceNationalId, Ct);

        await Registrations.CancelAsync(user, receipt.RegistrationId, Ct);

        await using (var context = _db.CreateContext())
        {
            var row = await context.Registrations.SingleAsync(r => r.Id == receipt.RegistrationId, Ct);
            Assert.Equal(RegistrationStatus.Cancelled, row.Status);
            Assert.Equal(Start.UtcDateTime, row.CancelledAt);
            Assert.Equal(1, (await context.Events.SingleAsync(e => e.Id == SmallEventId, Ct)).RegisteredCount);
        }

        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.CancelAsync(user, receipt.RegistrationId, Ct));
        Assert.Equal("Bu kayıt zaten iptal edilmiş.", again.Message);
        Assert.False((await Registrations.RegisterAsync(Admin(SeedAdminId), SmallEventId, MertNationalId, Ct)).Waitlisted);
        Assert.Equal(1, (await Registrations.RegisterAsync(user, SmallEventId, DenizNationalId, Ct)).QueuePosition); // full again
    }

    /// <summary>
    /// The same participant is cancelled and registered again several times: every new registration
    /// succeeds, the cancelled rows stay as history, and the seat counter always equals the number of
    /// active registrations.
    /// </summary>
    [Fact]
    public async Task Cancel_SameParticipantRegistersAgain_CounterMatchesActiveRegistrations()
    {
        var user = User(SeedUserId);
        var other = await Registrations.RegisterAsync(user, SmallEventId, EceNationalId, Ct);
        var ids = new List<int>();

        for (var round = 0; round < 3; round++)
        {
            var receipt = await Registrations.RegisterAsync(user, SmallEventId, DenizNationalId, Ct);
            ids.Add(receipt.RegistrationId);
            Assert.Equal(2, await ActiveAndCounterAsync());
            await Registrations.CancelAsync(user, receipt.RegistrationId, Ct);
            Assert.Equal(1, await ActiveAndCounterAsync());
        }

        var final = await Registrations.RegisterAsync(user, SmallEventId, DenizNationalId, Ct);
        Assert.Equal(2, await ActiveAndCounterAsync());
        Assert.Equal(ids.Count + 1, ids.Append(final.RegistrationId).Distinct().Count());
        Assert.Contains(await Registrations.GetMyRegistrationsAsync(user, Ct), r => r.Id == final.RegistrationId);
        Assert.Contains(await Registrations.GetMyRegistrationsAsync(user, Ct), r => r.Id == other.RegistrationId);
        await using var context = _db.CreateContext();
        Assert.Equal(3, await context.Registrations.CountAsync(r => r.EventId == SmallEventId && r.Status == RegistrationStatus.Cancelled, Ct));
    }

    /// <summary>The active registrations of the small event, after checking that its seat counter is the same.</summary>
    private async Task<int> ActiveAndCounterAsync()
    {
        await using var context = _db.CreateContext();
        var active = await context.Registrations.CountAsync(r => r.EventId == SmallEventId && r.Status == RegistrationStatus.Active, Ct);
        Assert.Equal(active, (await context.Events.SingleAsync(e => e.Id == SmallEventId, Ct)).RegisteredCount);
        return active;
    }

    [Fact]
    public async Task Cancel_PastEvent_IsRefused()
    {
        var id = await Events.CreateAsync(Admin(SeedAdminId), "Bugünkü Etkinlik", new DateTime(2026, 3, 2), 5, Ct);
        var receipt = await Registrations.RegisterAsync(User(SeedUserId), id, DenizNationalId, Ct);
        _db.Clock.Advance(TimeSpan.FromDays(1));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.CancelAsync(User(SeedUserId), receipt.RegistrationId, Ct));

        Assert.Equal("Geçmiş bir etkinliğin kaydı iptal edilemez.", error.Message);
        Assert.Single(await Registrations.GetMyRegistrationsAsync(User(SeedUserId), Ct));
    }

    [Fact]
    public async Task Repository_Cancel_RepeatsTheOwnerAndDateConditionsInTheUpdate()
    {
        var repository = _db.Get<IRegistrationRepository>();
        var id = (await repository.AddAsync(Access.ActorOf(Admin(SeedAdminId)), 1, ConcertId, () => "ETK-AAAAAAAAAA", Ct)).RegistrationId;
        var other = (await repository.AddAsync(Access.ActorOf(Admin(SeedAdminId)), 2, ConcertId, () => "ETK-BBBBBBBBBB", Ct)).RegistrationId;
        static string NoCode() => throw new InvalidOperationException("Nobody waits, so no ticket is issued.");

        Assert.Equal(CancellationOutcome.NotCancelled, await repository.CancelAsync(Access.ActorOf(Admin(SeedAdminId)), id, SeedAdminId, NoCode, Ct)); // participant 1 belongs to user 2
        Assert.Equal(CancellationOutcome.Cancelled, await repository.CancelAsync(Access.ActorOf(User(SeedUserId)), id, SeedUserId, NoCode, Ct));
        Assert.Equal(CancellationOutcome.NotCancelled, await repository.CancelAsync(Access.ActorOf(Admin(SeedAdminId)), id, null, NoCode, Ct));
        Assert.Equal(CancellationOutcome.NotCancelled, await repository.CancelAsync(Access.ActorOf(Admin(SeedAdminId)), 404, null, NoCode, Ct));

        // The day is the database's at the write: past the concert's day nothing is cancelled any more.
        _db.Clock.MoveTo(new DateOnly(2027, 4, 18));
        Assert.Equal(CancellationOutcome.NotCancelled, await repository.CancelAsync(Access.ActorOf(User(SeedUserId)), other, SeedUserId, NoCode, Ct));

        await using var context = _db.CreateContext();
        Assert.Equal(1, (await context.Events.SingleAsync(e => e.Id == ConcertId, Ct)).RegisteredCount);
    }

    [Fact]
    public async Task ParticipantsOfAnEvent_AreForAdminsOnlyAndMasked()
    {
        var receipt = await Registrations.RegisterAsync(User(SeedUserId), ConcertId, DenizNationalId, Ct);

        var list = await Participants.GetByEventAsync(Admin(SeedAdminId), ConcertId, Ct);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Participants.GetByEventAsync(User(SeedUserId), ConcertId, Ct));

        var row = Assert.Single(list);
        Assert.Equal("Deniz Örnek", row.FullName);
        Assert.Equal("100******46", row.MaskedNationalId);
        Assert.Equal("Giriş yapılmadı", row.State);
        Assert.NotNull(receipt.TicketCode);
        Assert.DoesNotContain(receipt.TicketCode, row.ToString(), StringComparison.Ordinal);
    }

    private async Task<UserSession> RegisterAndSignInAsync(string userName)
    {
        await Auth.RegisterAsync(userName, "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        return await Auth.SignInAsync(userName, "Gizli.Sifre1", Ct);
    }
}
