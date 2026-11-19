using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Business.Auditing;

/// <summary>
/// Security relevant events. An event carries only its kind and the role of the account involved: no
/// account id, user name, participant name, e-mail address, national id, password, setup key or ticket
/// code, so an audit entry cannot identify a person (KVKK). The entries can only be counted.
/// </summary>
public enum AuditEvent
{
    /// <summary>A sign-in succeeded.</summary>
    SignInSucceeded = 1,

    /// <summary>
    /// A sign-in failed: unknown account, wrong password, locked account or a lock placed by a
    /// concurrent failure. The cases share one event, written without a role, on purpose: the log file
    /// is readable by the local Windows user, and a separate lock event or the account's role would
    /// tell that user which accounts exist or are locked.
    /// </summary>
    SignInFailed = 2,

    /// <summary>A plain text or weaker stored password was replaced with a current hash.</summary>
    PasswordUpgraded = 3,

    /// <summary>A new account was created through the sign-up form.</summary>
    AccountRegistered = 4,

    /// <summary>The first admin account was created by the first-run setup.</summary>
    FirstAdminCreated = 5,

    /// <summary>A setup key was refused (wrong, expired, used or revoked); the key itself is never written.</summary>
    SetupKeyRefused = 6,

    /// <summary>An admin created an event.</summary>
    EventCreated = 7,

    /// <summary>An account added a participant to its list.</summary>
    ParticipantAdded = 8,

    /// <summary>A participant was registered for an event and a ticket was issued.</summary>
    RegistrationCreated = 9,

    /// <summary>
    /// A registration lost the race for the last seat or found the event full. No longer recorded: a
    /// full event puts the participant on its waiting list (<see cref="RegistrationWaitlisted"/>); kept
    /// so the value keeps its meaning.
    /// </summary>
    RegistrationEventFull = 10,

    /// <summary>A registration was cancelled.</summary>
    RegistrationCancelled = 11,

    /// <summary>An account asked for data or an operation it is not allowed to use.</summary>
    AccessDenied = 12,

    /// <summary>
    /// An open session was refused because its account was deleted, disabled or given another role
    /// since the sign-in; the role is the one the session was issued with.
    /// </summary>
    SessionRejected = 13,

    /// <summary>An admin disabled an account (the role is the acting admin's).</summary>
    AccountDisabled = 14,

    /// <summary>An admin enabled an account again (the role is the acting admin's).</summary>
    AccountEnabled = 15,

    /// <summary>An admin changed the role of another account (the role is the acting admin's).</summary>
    RoleChanged = 16,

    /// <summary>A participant was put on the waiting list of a full event.</summary>
    RegistrationWaitlisted = 17,

    /// <summary>A cancellation freed a seat and the first waiting participant moved up to it with a new ticket.</summary>
    RegistrationPromoted = 18,

    /// <summary>An admin changed the date, capacity or description of an event.</summary>
    EventUpdated = 19,

    /// <summary>An admin cancelled an event; its registrations and waiting places were closed.</summary>
    EventCancelled = 20,
}

/// <summary>Receives <see cref="AuditEvent"/>s; the application writes them to its log file.</summary>
public interface IAuditLog
{
    /// <summary>Records <paramref name="auditEvent"/>; <paramref name="role"/> is <see langword="null"/> when no account is known.</summary>
    void Record(AuditEvent auditEvent, UserRole? role);
}

public static class AuditCodes
{
    /// <summary>Stable, lower case code of <paramref name="auditEvent"/> as it is written to the log.</summary>
    public static string Of(AuditEvent auditEvent) => auditEvent switch
    {
        AuditEvent.SignInSucceeded => "auth.sign_in.succeeded",
        AuditEvent.SignInFailed => "auth.sign_in.failed",
        AuditEvent.PasswordUpgraded => "auth.password.upgraded",
        AuditEvent.AccountRegistered => "auth.account.registered",
        AuditEvent.FirstAdminCreated => "setup.first_admin.created",
        AuditEvent.SetupKeyRefused => "setup.key.refused",
        AuditEvent.EventCreated => "event.created",
        AuditEvent.ParticipantAdded => "participant.added",
        AuditEvent.RegistrationCreated => "registration.created",
        AuditEvent.RegistrationEventFull => "registration.event_full",
        AuditEvent.RegistrationCancelled => "registration.cancelled",
        AuditEvent.AccessDenied => "access.denied",
        AuditEvent.SessionRejected => "auth.session.rejected",
        AuditEvent.AccountDisabled => "admin.account.disabled",
        AuditEvent.AccountEnabled => "admin.account.enabled",
        AuditEvent.RoleChanged => "admin.account.role_changed",
        AuditEvent.RegistrationWaitlisted => "registration.waitlisted",
        AuditEvent.RegistrationPromoted => "registration.promoted",
        AuditEvent.EventUpdated => "event.updated",
        AuditEvent.EventCancelled => "event.cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(auditEvent), auditEvent, "Unknown audit event."),
    };

    /// <summary>Lower case role name as it is written to the log; "none" when no account is known.</summary>
    public static string Of(UserRole? role) => role switch
    {
        UserRole.Admin => "admin",
        UserRole.User => "user",
        null => "none",
        _ => "unknown",
    };
}

/// <summary>Discards every event; used when no audit sink is registered, for example in tests.</summary>
internal sealed class NullAuditLog : IAuditLog
{
    public static readonly NullAuditLog Instance = new();

    public void Record(AuditEvent auditEvent, UserRole? role)
    {
    }
}
