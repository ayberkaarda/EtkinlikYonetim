using EtkinlikYonetim.Business.Auditing;

namespace EtkinlikYonetim.Business.Services;

/// <summary>Role checks and the local calendar day shared by the services.</summary>
internal static class Access
{
    public static void RequireSignedIn(UserSession? session) => ArgumentNullException.ThrowIfNull(session);

    /// <summary>Refuses every account but an admin; the refusal is audited with the caller's role.</summary>
    public static void RequireAdmin(UserSession? session, IAuditLog audit)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsAdmin)
        {
            audit.Record(AuditEvent.AccessDenied, session.Role);
            throw new AccessDeniedException();
        }
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
