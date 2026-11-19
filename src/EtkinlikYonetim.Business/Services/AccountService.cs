using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Services;

/// <summary>
/// Account management for admins: listing the accounts, disabling and enabling them and changing their
/// role. An admin cannot change their own account here, and the last enabled admin can neither be
/// disabled nor made a user; the database decides both in the transaction that writes.
/// </summary>
public interface IAccountService
{
    /// <summary>Every account with its role and state; admins only.</summary>
    Task<IReadOnlyList<AccountSummary>> GetAccountsAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Enables or disables another account; admins only. A disabled account's open sessions stop working.</summary>
    Task SetEnabledAsync(UserSession session, int userId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Gives another account the <paramref name="role"/>; admins only. A changed account's open sessions stop working.</summary>
    Task ChangeRoleAsync(UserSession session, int userId, UserRole role, CancellationToken cancellationToken = default);
}

internal sealed class AccountService(IUserRepository users, ISessionGuard sessions, IAuditLog audit) : IAccountService
{
    internal const string OwnAccount = "Kendi hesabınızın rolünü veya durumunu değiştiremezsiniz.";
    internal const string AccountNotFound = "Hesap bulunamadı.";
    internal const string LastActiveAdmin = "Son etkin yönetici hesabı devre dışı bırakılamaz veya kullanıcı rolüne alınamaz.";
    internal const string RoleInvalid = "Geçersiz rol.";

    public async Task<IReadOnlyList<AccountSummary>> GetAccountsAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);
        return await users.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(UserSession session, int userId, bool enabled, CancellationToken cancellationToken = default)
    {
        await RequireOtherAccountAsync(session, userId, cancellationToken).ConfigureAwait(false);

        var status = await users.SetDisabledAsync(Access.ActorOf(session), userId, !enabled, cancellationToken).ConfigureAwait(false);
        Complete(status, session, enabled ? AuditEvent.AccountEnabled : AuditEvent.AccountDisabled);
    }

    public async Task ChangeRoleAsync(UserSession session, int userId, UserRole role, CancellationToken cancellationToken = default)
    {
        await RequireOtherAccountAsync(session, userId, cancellationToken).ConfigureAwait(false);
        if (!Enum.IsDefined(role))
        {
            throw new BusinessRuleException(RoleInvalid);
        }

        var status = await users.SetRoleAsync(Access.ActorOf(session), userId, role, cancellationToken).ConfigureAwait(false);
        Complete(status, session, AuditEvent.RoleChanged);
    }

    /// <summary>
    /// Admins only, checked against the database, and never the caller's own account: a user cannot
    /// raise their own role, and an admin cannot lock themselves out by mistake.
    /// </summary>
    private async Task RequireOtherAccountAsync(UserSession session, int userId, CancellationToken cancellationToken)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);
        if (userId == session.UserId)
        {
            throw new BusinessRuleException(OwnAccount);
        }
    }

    private void Complete(AccountChangeStatus status, UserSession session, AuditEvent done)
    {
        switch (status)
        {
            case AccountChangeStatus.Changed:
                audit.Record(done, session.Role);
                return;
            case AccountChangeStatus.ActorNotAuthorized:
                // Demoted or disabled between the session check and the write.
                audit.Record(AuditEvent.SessionRejected, session.Role);
                throw new SessionExpiredException();
            case AccountChangeStatus.LastActiveAdmin:
                throw new BusinessRuleException(LastActiveAdmin);
            default:
                throw new BusinessRuleException(AccountNotFound);
        }
    }
}
