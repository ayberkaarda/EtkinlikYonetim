using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Moq;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>Account management: admins only, never the own account, never the last enabled admin.</summary>
public sealed class AccountServiceTests
{
    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly RecordingAudit _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_CannotRaiseTheirOwnRole()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(
            () => Service().ChangeRoleAsync(Sessions.User, Sessions.User.UserId, UserRole.Admin, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(
            () => Service().SetEnabledAsync(Sessions.User, Sessions.OtherUser.UserId, false, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetAccountsAsync(Sessions.User, Ct));

        _users.VerifyNoOtherCalls();
        Assert.All(_audit.Events, e => Assert.Equal((AuditEvent.AccessDenied, (UserRole?)UserRole.User), e));
    }

    [Fact]
    public async Task Admin_CannotChangeTheirOwnAccount()
    {
        var role = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().ChangeRoleAsync(Sessions.Admin, Sessions.Admin.UserId, UserRole.User, Ct));
        var state = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().SetEnabledAsync(Sessions.Admin, Sessions.Admin.UserId, false, Ct));

        Assert.Equal(AccountService.OwnAccount, role.Message);
        Assert.Equal(AccountService.OwnAccount, state.Message);
        _users.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(AccountChangeStatus.LastActiveAdmin, nameof(AccountService.LastActiveAdmin))]
    [InlineData(AccountChangeStatus.NotFound, nameof(AccountService.AccountNotFound))]
    public async Task RefusedChanges_AreReportedAndNotAudited(AccountChangeStatus status, string expected)
    {
        _users.Setup(u => u.SetRoleAsync(It.Is<Actor>(a => a == new Actor(Sessions.Admin.UserId, UserRole.Admin, 0)), 7, UserRole.User, It.IsAny<CancellationToken>())).ReturnsAsync(status);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().ChangeRoleAsync(Sessions.Admin, 7, UserRole.User, Ct));

        Assert.Equal(expected == nameof(AccountService.LastActiveAdmin) ? AccountService.LastActiveAdmin : AccountService.AccountNotFound, error.Message);
        Assert.Empty(_audit.Events);
    }

    /// <summary>The database found the acting admin demoted or disabled when it wrote: the session ends.</summary>
    [Fact]
    public async Task Change_ByAnAdminDemotedMeanwhile_EndsTheSession()
    {
        _users.Setup(u => u.SetDisabledAsync(It.Is<Actor>(a => a == new Actor(Sessions.Admin.UserId, UserRole.Admin, 0)), 7, true, It.IsAny<CancellationToken>())).ReturnsAsync(AccountChangeStatus.ActorNotAuthorized);

        await Assert.ThrowsAsync<SessionExpiredException>(() => Service().SetEnabledAsync(Sessions.Admin, 7, false, Ct));

        Assert.Equal([(AuditEvent.SessionRejected, (UserRole?)UserRole.Admin)], _audit.Events);
    }

    [Fact]
    public async Task Changes_AreStoredForTheActingAdminAndAudited()
    {
        _users.Setup(u => u.SetDisabledAsync(It.Is<Actor>(a => a == new Actor(Sessions.Admin.UserId, UserRole.Admin, 0)), 7, true, It.IsAny<CancellationToken>())).ReturnsAsync(AccountChangeStatus.Changed);
        _users.Setup(u => u.SetDisabledAsync(It.Is<Actor>(a => a == new Actor(Sessions.Admin.UserId, UserRole.Admin, 0)), 7, false, It.IsAny<CancellationToken>())).ReturnsAsync(AccountChangeStatus.Changed);
        _users.Setup(u => u.SetRoleAsync(It.Is<Actor>(a => a == new Actor(Sessions.Admin.UserId, UserRole.Admin, 0)), 7, UserRole.Admin, It.IsAny<CancellationToken>())).ReturnsAsync(AccountChangeStatus.Changed);

        await Service().SetEnabledAsync(Sessions.Admin, 7, false, Ct);
        await Service().SetEnabledAsync(Sessions.Admin, 7, true, Ct);
        await Service().ChangeRoleAsync(Sessions.Admin, 7, UserRole.Admin, Ct);

        Assert.Equal(
            [(AuditEvent.AccountDisabled, UserRole.Admin), (AuditEvent.AccountEnabled, UserRole.Admin), (AuditEvent.RoleChanged, (UserRole?)UserRole.Admin)],
            _audit.Events);
    }

    [Fact]
    public async Task UnknownRole_IsRefused()
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().ChangeRoleAsync(Sessions.Admin, 7, (UserRole)5, Ct));

        Assert.Equal(AccountService.RoleInvalid, error.Message);
        _users.VerifyNoOtherCalls();
    }

    private AccountService Service() => ServiceFactory.Account(_users.Object, _audit);
}
