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

        Assert.Matches("^ETK-[A-HJ-NP-Z2-9]{10}$", receipt.TicketCode);
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

        Assert.Equal("Bu katılımcı bu etkinliğe zaten kayıtlı.", error.Message);
        Assert.Equal(1, (await Events.GetEventsAsync(User(SeedUserId), Ct)).Single(e => e.Id == ConcertId).RegisteredCount);
    }

    [Fact]
    public async Task Repository_SecondRegistrationOfTheSameParticipant_IsReportedAndTheSeatIsKept()
    {
        var repository = _db.Get<IRegistrationRepository>();
        var today = DateOnly.FromDateTime(Start.DateTime);
        await repository.AddAsync(1, ConcertId, today, Start.UtcDateTime, "ETK-AAAAAAAAAA", Ct);

        await Assert.ThrowsAsync<AlreadyRegisteredException>(() => repository.AddAsync(1, ConcertId, today, Start.UtcDateTime, "ETK-BBBBBBBBBB", Ct));

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
    public async Task Register_FullEvent_IsRejected()
    {
        var admin = Admin(SeedAdminId);
        await Registrations.RegisterAsync(admin, SmallEventId, DenizNationalId, Ct);
        await Registrations.RegisterAsync(admin, SmallEventId, EceNationalId, Ct);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(admin, SmallEventId, MertNationalId, Ct));

        Assert.Equal("Etkinliğin kontenjanı dolu.", error.Message);
        Assert.Equal(2, (await Events.GetEventsAsync(admin, Ct)).Single(e => e.Id == SmallEventId).RegisteredCount);
    }

    [Fact]
    public async Task Repository_FullOrPastEvent_IsRefusedInsideTheTransaction()
    {
        var repository = _db.Get<IRegistrationRepository>();
        var today = DateOnly.FromDateTime(Start.DateTime);
        await repository.AddAsync(1, SmallEventId, today, Start.UtcDateTime, "ETK-AAAAAAAAAA", Ct);
        await repository.AddAsync(2, SmallEventId, today, Start.UtcDateTime, "ETK-BBBBBBBBBB", Ct);

        await Assert.ThrowsAsync<EventFullException>(() => repository.AddAsync(3, SmallEventId, today, Start.UtcDateTime, "ETK-CCCCCCCCCC", Ct));
        await Assert.ThrowsAsync<EventClosedException>(() => repository.AddAsync(3, ConcertId, new DateOnly(2027, 4, 18), Start.UtcDateTime, "ETK-DDDDDDDDDD", Ct));
        await Assert.ThrowsAsync<EventClosedException>(() => repository.AddAsync(3, 999, today, Start.UtcDateTime, "ETK-EEEEEEEEEE", Ct));

        await using var context = _db.CreateContext();
        Assert.Equal(2, await context.Registrations.CountAsync(Ct));
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

        var duplicate = await Assert.ThrowsAsync<BusinessRuleException>(() => Participants.AddAsync(Admin(SeedAdminId), NewNationalId, "Başka Biri", "baska@example.com", Ct));
        Assert.Equal("Bu T.C. Kimlik No ile kayıtlı bir katılımcı zaten var.", duplicate.Message);
        await Registrations.RegisterAsync(user, ConcertId, NewNationalId, Ct);
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
        await Registrations.RegisterAsync(Admin(SeedAdminId), SmallEventId, MertNationalId, Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(user, SmallEventId, DenizNationalId, Ct));
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
        var today = DateOnly.FromDateTime(Start.DateTime);
        var id = await repository.AddAsync(1, ConcertId, today, Start.UtcDateTime, "ETK-AAAAAAAAAA", Ct);

        Assert.False(await repository.CancelAsync(id, SeedAdminId, today, Start.UtcDateTime, Ct)); // participant 1 belongs to user 2
        Assert.False(await repository.CancelAsync(id, SeedUserId, new DateOnly(2027, 4, 18), Start.UtcDateTime, Ct));
        Assert.True(await repository.CancelAsync(id, SeedUserId, today, Start.UtcDateTime, Ct));
        Assert.False(await repository.CancelAsync(id, null, today, Start.UtcDateTime, Ct));

        await using var context = _db.CreateContext();
        Assert.Equal(0, (await context.Events.SingleAsync(e => e.Id == ConcertId, Ct)).RegisteredCount);
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
        Assert.Equal(receipt.TicketCode, row.TicketCode);
    }

    private async Task<UserSession> RegisterAndSignInAsync(string userName)
    {
        await Auth.RegisterAsync(userName, "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        return await Auth.SignInAsync(userName, "Gizli.Sifre1", Ct);
    }
}
