using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Services;

/// <summary>Session and role checks and the database's time shared by the services.</summary>
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

    /// <summary>The account a write of <paramref name="session"/> acts for, checked again by the repository inside the write.</summary>
    public static Actor ActorOf(UserSession session) => new(session.UserId, session.Role, session.SessionVersion);

    /// <summary>
    /// Runs a write as the account of <paramref name="session"/>. When the repository finds, after its locks
    /// and inside its transaction, that the account was disabled, demoted or changed since the session check,
    /// nothing was written: the refusal is audited and the session ends like one the guard rejects.
    /// </summary>
    public static async Task<T> WriteAsAsync<T>(this IAuditLog audit, UserSession session, Func<Actor, Task<T>> write)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(write);
        try
        {
            return await write(ActorOf(session)).ConfigureAwait(false);
        }
        catch (ActorRevokedException ex)
        {
            audit.Record(AuditEvent.SessionRejected, session.Role);
            throw new SessionExpiredException(SessionExpiredException.DefaultMessage, ex);
        }
    }

    /// <summary>
    /// The database's UTC time now and the calendar day it falls on in this computer's time zone (event
    /// dates are local calendar days). Computers whose clocks are wrong still agree on both, as long as
    /// their time zone is set right; the computer's own clock plays no part.
    /// </summary>
    public static async Task<DatabaseNow> NowAsync(this IDatabaseClock clock, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var utc = DateTime.SpecifyKind(await clock.GetUtcNowAsync(cancellationToken).ConfigureAwait(false), DateTimeKind.Utc);
        return new DatabaseNow(utc, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, timeProvider.LocalTimeZone)));
    }
}

/// <summary>The database's UTC time and its local calendar day, read once for one operation.</summary>
internal readonly record struct DatabaseNow(DateTime Utc, DateOnly Today);
