using System.Security.Cryptography;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using FluentValidation;

namespace EtkinlikYonetim.Business.Services;

/// <summary>
/// The outcome of a registration: a seat with its ticket, or, when the event is full, a place in its
/// waiting list (<see cref="QueuePosition"/>, 1 is next) and no ticket yet.
/// </summary>
public sealed record RegistrationReceipt(int RegistrationId, string? TicketCode, string EventName, DateOnly EventDate, int? QueuePosition = null)
{
    /// <summary>Whether the participant was put on the waiting list instead of getting a seat.</summary>
    public bool Waitlisted => QueuePosition is not null;
}

/// <summary>
/// Registering participants for events, listing and cancelling registrations. Users act only on the
/// participants they added; admins act on all of them.
/// </summary>
public interface IRegistrationService
{
    /// <summary>
    /// Registers the participant with <paramref name="nationalId"/> for the event and issues a ticket, or
    /// puts the participant on the waiting list when the event is full.
    /// </summary>
    Task<RegistrationReceipt> RegisterAsync(
        UserSession session,
        int eventId,
        string? nationalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Seats and waiting places the session may see: its own participants', or all for an admin, who sees the
    /// ticket codes of the participants it added only.
    /// </summary>
    Task<IReadOnlyList<RegistrationDetails>> GetMyRegistrationsAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a registration or a waiting place the session may see. A freed seat goes to the first
    /// waiting participant at once, with a new ticket.
    /// </summary>
    Task CancelAsync(UserSession session, int registrationId, CancellationToken cancellationToken = default);
}

internal sealed class RegistrationService(
    IRegistrationRepository registrations,
    IParticipantRepository participants,
    IEventRepository events,
    IDatabaseClock clock,
    TimeProvider timeProvider,
    IValidator<RegistrationRequestInput> requestValidator,
    IValidator<RegistrationTargetInput> targetValidator,
    IValidator<CancellationInput> cancellationValidator,
    ISessionGuard sessions,
    IAuditLog audit) : IRegistrationService
{
    internal const string EventRequired = "Lütfen bir etkinlik seçin.";
    internal const string NationalIdFormat = "Geçerli bir 11 haneli TC Kimlik No girin.";
    internal const string ParticipantNotFound = "Bu TC Kimlik numarasıyla sistemde kayıtlı bir katılımcı bulunamadı.";
    internal const string ParticipantAmbiguous = "Bu TC Kimlik numarası birden fazla hesabın katılımcısıyla eşleşiyor. Kaydı katılımcıyı ekleyen hesapla yapın.";
    internal const string PastEvent = "Geçmiş tarihli bir etkinliğe kayıt yapılamaz.";
    internal const string CancelledEvent = "İptal edilmiş bir etkinliğe kayıt yapılamaz.";
    internal const string AlreadyRegistered = "Bu katılımcı bu etkinliğe zaten kayıtlı veya bekleme listesinde.";
    internal const string AlreadyCancelled = "Bu kayıt zaten iptal edilmiş.";
    internal const string PastCannotCancel = "Geçmiş bir etkinliğin kaydı iptal edilemez.";
    internal const string CheckedInCannotCancel = "Giriş yapılmış bir kayıt iptal edilemez.";
    internal const string CancelFailed = "Kayıt iptal edilemedi.";

    public async Task<RegistrationReceipt> RegisterAsync(
        UserSession session,
        int eventId,
        string? nationalId,
        CancellationToken cancellationToken = default)
    {
        await sessions.RequireCurrentAsync(session, cancellationToken).ConfigureAwait(false);

        var request = RegistrationRequestInput.Create(eventId, nationalId);
        requestValidator.ValidateOrThrow(request);

        try
        {
            return await RegisterValidAsync(session, request, cancellationToken).ConfigureAwait(false);
        }
        catch (BusinessRuleException)
        {
            // Every refusal of a valid request records one event; the log file writes it with the code of
            // a success, so it cannot tell which national ids or registrations exist.
            audit.Record(AuditEvent.RegistrationRefused, session.Role);
            throw;
        }
    }

    private async Task<RegistrationReceipt> RegisterValidAsync(UserSession session, RegistrationRequestInput request, CancellationToken cancellationToken)
    {
        var eventId = request.EventId;

        // Users look only in their own list; admins see every account's participants and prefer their own.
        var candidates = await participants.FindByNationalIdAsync(request.NationalId, Access.OwnerFilter(session), cancellationToken).ConfigureAwait(false);
        var participant = candidates.FirstOrDefault(p => p.OwnerUserId == session.UserId)
            ?? (candidates.Count == 1 ? candidates[0] : null);
        if (participant is null && session.IsAdmin && candidates.Count > 1)
        {
            throw new BusinessRuleException(ParticipantAmbiguous);
        }

        // Somebody else's participant gets the same answer as an unknown one, so national ids cannot be probed.
        if (participant is null || !(session.IsAdmin || participant.OwnerUserId == session.UserId))
        {
            throw new BusinessRuleException(ParticipantNotFound);
        }

        var target = await events.GetByIdAsync(eventId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException(EventRequired);
        if (target.Cancelled)
        {
            throw new BusinessRuleException(CancelledEvent);
        }

        var now = await clock.NowAsync(timeProvider, cancellationToken).ConfigureAwait(false);
        targetValidator.ValidateOrThrow(new RegistrationTargetInput(target.Date, now.Today));

        string? code = null;
        try
        {
            // The repository decides between a seat and the waiting list and repeats the date and
            // duplicate checks inside its transaction; the checks above only give the common cases a
            // quick answer. The ticket code is drawn only when a seat is taken.
            var outcome = await registrations.AddAsync(
                participant.Id,
                eventId,
                now.Today,
                now.Utc,
                () => code = TicketCode.New(),
                cancellationToken).ConfigureAwait(false);
            if (outcome.Status == RegistrationStatus.Waiting)
            {
                audit.Record(AuditEvent.RegistrationWaitlisted, session.Role);
                return new RegistrationReceipt(outcome.RegistrationId, null, target.Name, target.Date, outcome.QueuePosition ?? 1);
            }

            audit.Record(AuditEvent.RegistrationCreated, session.Role);
            return new RegistrationReceipt(outcome.RegistrationId, code, target.Name, target.Date);
        }
        catch (AlreadyRegisteredException ex)
        {
            throw new BusinessRuleException(AlreadyRegistered, ex);
        }
        catch (EventClosedException ex)
        {
            throw new BusinessRuleException(PastEvent, ex);
        }
    }

    public async Task<IReadOnlyList<RegistrationDetails>> GetMyRegistrationsAsync(
        UserSession session,
        CancellationToken cancellationToken = default)
    {
        await sessions.RequireCurrentAsync(session, cancellationToken).ConfigureAwait(false);
        var list = await registrations.GetActiveAsync(Access.OwnerFilter(session), cancellationToken).ConfigureAwait(false);

        // A ticket code is the participant's secret: an admin sees the registrations of every account, but
        // the code only of participants the admin added or that belong to no account.
        return session.IsAdmin
            ? list.Select(r => r.OwnerUserId is null || r.OwnerUserId == session.UserId ? r : r with { TicketCode = null }).ToList()
            : list;
    }

    public async Task CancelAsync(UserSession session, int registrationId, CancellationToken cancellationToken = default)
    {
        await sessions.RequireCurrentAsync(session, cancellationToken).ConfigureAwait(false);

        var registration = await registrations.GetOwnershipAsync(registrationId, cancellationToken).ConfigureAwait(false);

        // A missing registration and somebody else's registration get the same answer, so ids cannot be probed.
        if (registration is null || !(session.IsAdmin || registration.OwnerUserId == session.UserId))
        {
            audit.Record(AuditEvent.RegistrationCancelRefused, session.Role);
            throw new AccessDeniedException();
        }

        var now = await clock.NowAsync(timeProvider, cancellationToken).ConfigureAwait(false);
        var check = cancellationValidator.Validate(new CancellationInput(registration.Status, registration.EventDate, now.Today, registration.CheckedIn));
        if (!check.IsValid)
        {
            audit.Record(AuditEvent.RegistrationCancelRefused, session.Role);
            throw new BusinessRuleException(check.Errors[0].ErrorMessage);
        }

        // The repository repeats the ownership, status and date conditions in the same statement, so a
        // concurrent change between the read above and this update cannot slip through.
        var outcome = await registrations.CancelAsync(
            registrationId,
            Access.OwnerFilter(session),
            now.Today,
            now.Utc,
            TicketCode.New,
            cancellationToken).ConfigureAwait(false);
        if (outcome == CancellationOutcome.NotCancelled)
        {
            audit.Record(AuditEvent.RegistrationCancelRefused, session.Role);
            throw new BusinessRuleException(CancelFailed);
        }

        audit.Record(AuditEvent.RegistrationCancelled, session.Role);
        if (outcome == CancellationOutcome.CancelledAndPromoted)
        {
            // The role is the cancelling account's; the promoted participant may belong to anyone.
            audit.Record(AuditEvent.RegistrationPromoted, session.Role);
        }
    }
}

/// <summary>
/// Ticket codes: "ETK-" and 26 characters drawn by the operating system's cryptographic random number
/// generator from a 32 character alphabet, that is 130 random bits, so a code can be neither guessed nor
/// derived from another one. The code is the secret of the ticket: it is shown only to the participant's
/// account (and in its QR code), compared in constant time at the entrance and never written to a log.
/// </summary>
internal static class TicketCode
{
    public const string Prefix = "ETK-";
    public const int RandomLength = 26;

    /// <summary>Longest code the entrance accepts: new codes and the 32 hexadecimal characters of reissued old ones.</summary>
    public const int MaxLength = 40;

    // No 0/O, 1/I: the code is read and typed by people.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New() => Prefix + RandomNumberGenerator.GetString(Alphabet, RandomLength);

    /// <summary>
    /// The code as it is stored: trimmed and in upper case, so a hand-typed code in lower case matches.
    /// Returns <see langword="false"/> for anything that cannot be a ticket code (wrong prefix, length or characters).
    /// </summary>
    public static bool TryNormalize(string? typed, out string code)
    {
        code = (typed ?? string.Empty).Trim().ToUpperInvariant();
        return code.Length > Prefix.Length
            && code.Length <= MaxLength
            && code.StartsWith(Prefix, StringComparison.Ordinal)
            && code.AsSpan(Prefix.Length).ContainsAnyExcept(CodeCharacters) is false;
    }

    private static readonly System.Buffers.SearchValues<char> CodeCharacters =
        System.Buffers.SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
}