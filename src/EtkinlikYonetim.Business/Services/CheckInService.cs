using System.Security.Cryptography;
using System.Text;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Services;

/// <summary>What the entrance shows for a scanned or typed ticket code.</summary>
public enum CheckInOutcome
{
    /// <summary>The ticket was valid and unused; it is used now.</summary>
    CheckedIn = 0,

    /// <summary>The ticket was valid but has been used before.</summary>
    AlreadyUsed = 1,

    /// <summary>
    /// No such ticket, a ticket of another event, of a cancelled registration or of a cancelled event, or
    /// text that is no ticket code at all. The cases share one answer, so the entrance cannot be used to
    /// find out which codes exist.
    /// </summary>
    Rejected = 2,
}

/// <summary>The outcome of a check-in and the event's counter after it.</summary>
public sealed record CheckInResult(CheckInOutcome Outcome, CheckInCount Count);

/// <summary>The entrance: admins check tickets in for the event they chose.</summary>
public interface ICheckInService
{
    /// <summary>
    /// Checks the ticket with <paramref name="code"/> in for <paramref name="eventId"/>; admins only. A valid
    /// unused ticket is marked used by a conditional UPDATE, so of two scans of the same ticket exactly one
    /// gets <see cref="CheckInOutcome.CheckedIn"/>.
    /// </summary>
    Task<CheckInResult> CheckInAsync(UserSession session, int eventId, string? code, CancellationToken cancellationToken = default);

    /// <summary>The used tickets and the seats held of the event; admins only.</summary>
    Task<CheckInCount> GetCountAsync(UserSession session, int eventId, CancellationToken cancellationToken = default);
}

internal sealed class CheckInService(
    ITicketRepository tickets,
    IDatabaseClock clock,
    ISessionGuard sessions,
    IAuditLog audit) : ICheckInService
{
    private static readonly byte[] NoCode = new byte[32];

    public async Task<CheckInResult> CheckInAsync(UserSession session, int eventId, string? code, CancellationToken cancellationToken = default)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);

        var outcome = await DecideAsync(eventId, code, cancellationToken).ConfigureAwait(false);
        audit.Record(
            outcome switch
            {
                CheckInOutcome.CheckedIn => AuditEvent.TicketCheckedIn,
                CheckInOutcome.AlreadyUsed => AuditEvent.TicketAlreadyUsed,
                _ => AuditEvent.TicketRejected,
            },
            session.Role);
        var count = await tickets.GetCheckInCountAsync(eventId, cancellationToken).ConfigureAwait(false);
        return new CheckInResult(outcome, count);
    }

    public async Task<CheckInCount> GetCountAsync(UserSession session, int eventId, CancellationToken cancellationToken = default)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);
        return await tickets.GetCheckInCountAsync(eventId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CheckInOutcome> DecideAsync(int eventId, string? typed, CancellationToken cancellationToken)
    {
        var wellFormed = TicketCode.TryNormalize(typed, out var code);
        var given = Encoding.ASCII.GetBytes(wellFormed ? code : string.Empty);

        // The database looks the ticket up by the hash of the code, so its timing depends on the hash
        // only; the stored code is then compared with the given one in constant time.
        var ticket = wellFormed
            ? await tickets.FindForCheckInAsync(TicketCodeHash.Of(code), cancellationToken).ConfigureAwait(false)
            : null;
        var stored = ticket is null ? NoCode : Encoding.ASCII.GetBytes(ticket.Code);
        var matches = CryptographicOperations.FixedTimeEquals(stored, given) && ticket is not null;

        if (!matches
            || ticket!.EventId != eventId
            || ticket.EventCancelled
            || ticket.Status != RegistrationStatus.Active)
        {
            return CheckInOutcome.Rejected;
        }

        if (ticket.CheckedInAt is not null)
        {
            return CheckInOutcome.AlreadyUsed;
        }

        // The check-in time is the database's, so the clock of the scanning computer cannot change it.
        var checkedInAt = await clock.GetUtcNowAsync(cancellationToken).ConfigureAwait(false);
        if (await tickets.CheckInAsync(ticket.TicketId, eventId, checkedInAt, cancellationToken).ConfigureAwait(false))
        {
            return CheckInOutcome.CheckedIn;
        }

        // Lost to a concurrent scan of the same ticket, or the registration or event was cancelled since
        // the lookup: read again to tell the two apart.
        var now = await tickets.FindForCheckInAsync(TicketCodeHash.Of(code), cancellationToken).ConfigureAwait(false);
        return now is { CheckedInAt: not null, Status: RegistrationStatus.Active, EventCancelled: false } && now.EventId == eventId
            ? CheckInOutcome.AlreadyUsed
            : CheckInOutcome.Rejected;
    }
}
