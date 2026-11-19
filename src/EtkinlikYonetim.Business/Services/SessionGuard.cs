using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Services;

/// <summary>Decides, against the database and at the time of each operation, whether a session may still be used.</summary>
internal interface ISessionGuard
{
    /// <summary>
    /// Throws <see cref="SessionExpiredException"/> when the account of <paramref name="session"/> no
    /// longer exists, is disabled, has another role than the session was issued with, or had its access
    /// changed (disabled, enabled, role changed) since then.
    /// </summary>
    Task EnsureCurrentAsync(UserSession session, CancellationToken cancellationToken);
}

internal sealed class SessionGuard(IUserRepository users, IAuditLog audit) : ISessionGuard
{
    public async Task EnsureCurrentAsync(UserSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var state = await users.GetSessionStateAsync(session.UserId, cancellationToken).ConfigureAwait(false);
        // The version catches what the role and state alone would miss: an account disabled and enabled
        // again, or demoted and promoted again, since the session was issued.
        if (state is null || state.Disabled || state.Role != session.Role || state.SessionVersion != session.SessionVersion)
        {
            audit.Record(AuditEvent.SessionRejected, session.Role);
            throw new SessionExpiredException();
        }
    }
}
