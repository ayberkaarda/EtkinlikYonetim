using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Services;

/// <summary>Decides, against the database and at the time of each operation, whether a session may still be used.</summary>
internal interface ISessionGuard
{
    /// <summary>
    /// Throws <see cref="SessionExpiredException"/> when the account of <paramref name="session"/> no
    /// longer exists, is disabled or has another role than the session was issued with.
    /// </summary>
    Task EnsureCurrentAsync(UserSession session, CancellationToken cancellationToken);
}

internal sealed class SessionGuard(IUserRepository users, IAuditLog audit) : ISessionGuard
{
    public async Task EnsureCurrentAsync(UserSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var state = await users.GetSessionStateAsync(session.UserId, cancellationToken).ConfigureAwait(false);
        if (state is null || state.Disabled || state.Role != session.Role)
        {
            audit.Record(AuditEvent.SessionRejected, session.Role);
            throw new SessionExpiredException();
        }
    }
}
