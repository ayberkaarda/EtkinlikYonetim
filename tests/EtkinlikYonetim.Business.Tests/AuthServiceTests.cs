using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>Sign-in and sign-up with strict mocks: rejected input must never reach a repository.</summary>
public sealed class AuthServiceTests
{
    private const string UserName = "ornek";
    private static readonly DateTime Now = new(2026, 3, 2, 10, 10, 0, DateTimeKind.Utc);

    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly PasswordHasher _hasher = new(iterations: 1_000);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("", "Gizli.Sifre1")]
    [InlineData("   ", "Gizli.Sifre1")]
    [InlineData(UserName, "")]
    [InlineData(UserName, null)]
    [InlineData(null, "Gizli.Sifre1")]
    public async Task SignIn_MissingInput_IsRejectedWithoutTouchingTheRepository(string? userName, string? password)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(userName, password, Ct));

        Assert.Equal(AuthService.InputRequired, error.Message);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignIn_PasswordLongerThanTheLegacyColumn_IsRejectedWithoutTouchingTheRepository()
    {
        var tooLong = new string('x', PasswordPolicy.SignInMaxLength + 1);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, tooLong, Ct));

        Assert.Equal(AuthService.InputRequired, error.Message);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignIn_LegacyPasswordLongerThanTheRegistrationLimit_IsVerifiedAndRehashedTrimmed()
    {
        var legacy = new string('x', PasswordPolicy.SignInMaxLength);
        Setup(Account(legacy, format: PasswordFormat.LegacyPlainText));
        string? stored = null;
        _users.Setup(u => u.RecordSuccessfulLoginAsync(It.Is<AccountCredentials>(c => c.Id == 7), Now, It.IsNotNull<string>(), It.IsAny<CancellationToken>()))
            .Callback<AccountCredentials, DateTime, string?, CancellationToken>((_, _, hash, _) => stored = hash)
            .ReturnsAsync(true);

        var session = await Service().SignInAsync(UserName, legacy, Ct);

        Assert.Equal(7, session.UserId);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify(legacy, stored!, PasswordFormat.Pbkdf2));
    }

    [Fact]
    public async Task SignIn_UnknownAccount_GivesTheSameAnswerAsAWrongPassword()
    {
        _users.Setup(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>())).ReturnsAsync((AccountCredentials?)null);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, "Gizli.Sifre1", Ct));

        Assert.Equal(AuthService.CredentialsInvalid, error.Message);
        _users.Verify(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>()), Times.Once);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignIn_WrongPassword_CountsTheFailureAndLocksForFifteenMinutesAtTheLimit()
    {
        Setup(Account(_hasher.Hash("Gizli.Sifre1")));
        _users.Setup(u => u.RecordFailedLoginAsync(7, LoginPolicy.MaxFailedAttempts, Now.AddMinutes(15), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, "yanlis", Ct));

        Assert.Equal(AuthService.CredentialsInvalid, error.Message);
        Assert.Contains("5 hatalı denemeden sonra giriş 15 dakika engellenir", error.Message, StringComparison.Ordinal);
        _users.Verify(u => u.RecordFailedLoginAsync(7, 5, Now.AddMinutes(15), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SignIn_LockedAccount_IsRefusedEvenWithTheRightPasswordAndNothingIsWritten()
    {
        Setup(Account(_hasher.Hash("Gizli.Sifre1"), lockoutEnd: Now.AddMinutes(14)));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, "Gizli.Sifre1", Ct));

        Assert.Equal(AuthService.CredentialsInvalid, error.Message);
        _users.Verify(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>()), Times.Once);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignIn_ExpiredLockout_AllowsSignIn()
    {
        Setup(Account(_hasher.Hash("Gizli.Sifre1"), lockoutEnd: Now.AddSeconds(-1)));
        SetupSuccess(null, true);

        var session = await Service().SignInAsync(UserName, "Gizli.Sifre1", Ct);

        Assert.Equal(7, session.UserId);
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Admin)]
    public async Task SignIn_Success_ReturnsASessionWithTheStoredRole(UserRole role)
    {
        Setup(Account(_hasher.Hash("Gizli.Sifre1"), role: role));
        SetupSuccess(null, true);

        var session = await Service().SignInAsync($" {UserName} ", "Gizli.Sifre1", Ct);

        Assert.Equal(7, session.UserId);
        Assert.Equal(UserName, session.UserName);
        Assert.Equal(role, session.Role);
        Assert.Equal(role == UserRole.Admin, session.IsAdmin);
    }

    [Fact]
    public async Task SignIn_AccountLockedOrChangedBeforeTheSuccessIsWritten_IsRefused()
    {
        Setup(Account(_hasher.Hash("Gizli.Sifre1")));
        SetupSuccess(null, false);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, "Gizli.Sifre1", Ct));

        Assert.Equal(AuthService.CredentialsInvalid, error.Message);
    }

    [Fact]
    public async Task SignIn_WeakerHash_IsReplacedOnSuccess()
    {
        Setup(Account(new PasswordHasher(iterations: 1_000).Hash("Gizli.Sifre1")));
        _users.Setup(u => u.RecordSuccessfulLoginAsync(It.IsAny<AccountCredentials>(), Now, It.IsNotNull<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await new AuthService(_users.Object, new PasswordHasher(iterations: 2_000), _time).SignInAsync(UserName, "Gizli.Sifre1", Ct);

        _users.Verify(u => u.RecordSuccessfulLoginAsync(It.IsAny<AccountCredentials>(), Now, It.Is<string>(h => h.StartsWith("PBKDF2-SHA256$2000$", StringComparison.Ordinal)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", "Gizli.Sifre1", "Gizli.Sifre1", nameof(AuthService.RegisterInputRequired))]
    [InlineData("yeni", "", "", nameof(AuthService.RegisterInputRequired))]
    [InlineData("yeni1", "Gizli.Sifre1", "Gizli.Sifre1", nameof(AuthService.UserNameInvalid))]
    [InlineData("yeni uye", "Gizli.Sifre1", "Gizli.Sifre1", nameof(AuthService.UserNameInvalid))]
    [InlineData("yeni", "Gizli.Sifre1", "Gizli.Sifre2", nameof(AuthService.PasswordsDoNotMatch))]
    [InlineData("yeni", "kisa", "kisa", nameof(AuthService.PasswordInvalid))]
    public async Task Register_InvalidInput_IsRejectedWithoutTouchingTheRepository(string userName, string password, string repeat, string expected)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync(userName, password, repeat, Ct));

        Assert.Equal(Message(expected), error.Message);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_UserNameLimits_FollowTheSignUpField()
    {
        Assert.Equal(
            AuthService.UserNameInvalid,
            (await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync(new string('a', 33), "Gizli.Sifre1", "Gizli.Sifre1", Ct))).Message);
        Assert.Equal(
            AuthService.PasswordInvalid,
            (await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync("yeni", new string('x', 65), new string('x', 65), Ct))).Message);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_StoresAHashWithTheUserRole()
    {
        User? added = null;
        _users.Setup(u => u.UserNameExistsAsync("Şule", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((user, _) => added = user)
            .ReturnsAsync(9);

        await Service().RegisterAsync(" Şule ", "Gizli.Sifre1", "Gizli.Sifre1", Ct);

        Assert.NotNull(added);
        Assert.Equal("Şule", added.UserName);
        Assert.Equal(UserRole.User, added.Role);
        Assert.Equal(PasswordFormat.Pbkdf2, added.PasswordFormat);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("Gizli.Sifre1", added.PasswordHash, PasswordFormat.Pbkdf2));
    }

    [Fact]
    public async Task Register_TakenOrConcurrentlyTakenName_IsRejected()
    {
        _users.Setup(u => u.UserNameExistsAsync("alinmis", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.UserNameExistsAsync("yaris", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).ThrowsAsync(new DuplicateUserNameException());

        var taken = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync("alinmis", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        var raced = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync("yaris", "Gizli.Sifre1", "Gizli.Sifre1", Ct));

        Assert.Equal(AuthService.UserNameTaken, taken.Message);
        Assert.Equal(AuthService.UserNameTaken, raced.Message);
    }

    private static string Message(string name) => name switch
    {
        nameof(AuthService.RegisterInputRequired) => AuthService.RegisterInputRequired,
        nameof(AuthService.UserNameInvalid) => AuthService.UserNameInvalid,
        nameof(AuthService.PasswordsDoNotMatch) => AuthService.PasswordsDoNotMatch,
        nameof(AuthService.PasswordInvalid) => AuthService.PasswordInvalid,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static AccountCredentials Account(
        string passwordHash,
        PasswordFormat format = PasswordFormat.Pbkdf2,
        DateTime? lockoutEnd = null,
        UserRole role = UserRole.User) =>
        new(7, UserName, role, passwordHash, format, 0, lockoutEnd);

    private void Setup(AccountCredentials account) =>
        _users.Setup(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>())).ReturnsAsync(account);

    private void SetupSuccess(string? newHash, bool result) =>
        _users.Setup(u => u.RecordSuccessfulLoginAsync(It.Is<AccountCredentials>(c => c.Id == 7), Now, newHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private AuthService Service() => new(_users.Object, _hasher, _time);
}
