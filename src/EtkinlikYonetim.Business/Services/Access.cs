using EtkinlikYonetim.Business.Auditing;

namespace EtkinlikYonetim.Business.Services;

/// <summary>Session and role checks and the local calendar day shared by the services.</summary>
internal static class Access
{
    /// <summary>
    /// Requires a session whose account still exists, is enabled and has the session's role, read from
    /// the database now; the role the session was issued with is not trusted on its own.
    /// </summary>
    public static Task RequireCurrentAsync(this ISessionGuard guard, UserSession? session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        return guard.EnsureCurrentAsync(session, cancellationToken);
    }

    /// <summary>
    /// Refuses every account but an admin whose role and enabled state the database confirms now. A
    /// non-admin session is refused before the database is asked; the refusal is audited with its role.
    /// </summary>
    public static async Task RequireAdminAsync(this ISessionGuard guard, UserSession? session, IAuditLog audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsAdmin)
        {
            audit.Record(AuditEvent.AccessDenied, session.Role);
            throw new AccessDeniedException();
        }

        await guard.EnsureCurrentAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The account whose data the session may see: its own id, or <see langword="null"/> (everyone)
    /// for an admin.
    /// </summary>
    public static int? OwnerFilter(UserSession session) => session.IsAdmin ? null : session.UserId;

    /// <summary>Today's date on the local wall clock; event dates are local calendar days.</summary>
    public static DateOnly Today(this TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
}
