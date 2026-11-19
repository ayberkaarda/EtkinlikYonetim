using System.Security.Cryptography;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Services;

/// <summary>The outcome of a registration: the registration and the ticket issued with it.</summary>
public sealed record RegistrationReceipt(int RegistrationId, string TicketCode, string EventName, DateOnly EventDate);

/// <summary>
/// Registering participants for events, listing and cancelling registrations. Users act only on the
/// participants they added; admins act on all of them.
/// </summary>
public interface IRegistrationService
{
    /// <summary>Registers the participant with <paramref name="nationalId"/> for the event and issues a ticket.</summary>
    Task<RegistrationReceipt> RegisterAsync(
        UserSession session,
        int eventId,
        string? nationalId,
        CancellationToken cancellationToken = default);

    /// <summary>Active registrations the session may see: its own participants', or all for an admin.</summary>
    Task<IReadOnlyList<RegistrationDetails>> GetMyRegistrationsAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Cancels a registration the session may see; the seat becomes free again.</summary>
    Task CancelAsync(UserSession session, int registrationId, CancellationToken cancellationToken = default);
}

internal sealed class RegistrationService(
    IRegistrationRepository registrations,
    IParticipantRepository participants,
    IEventRepository events,
    TimeProvider timeProvider) : IRegistrationService
{
    internal const string EventRequired = "Lütfen bir etkinlik seçin.";
    internal const string NationalIdFormat = "Geçerli bir 11 haneli TC Kimlik No girin.";
    internal const string ParticipantNotFound = "Bu TC Kimlik numarasıyla sistemde kayıtlı bir katılımcı bulunamadı.";
    internal const string PastEvent = "Geçmiş tarihli bir etkinliğe kayıt yapılamaz.";
    internal const string EventFull = "Etkinliğin kontenjanı dolu.";
    internal const string AlreadyRegistered = "Bu katılımcı bu etkinliğe zaten kayıtlı.";
    internal const string AlreadyCancelled = "Bu kayıt zaten iptal edilmiş.";
    internal const string PastCannotCancel = "Geçmiş bir etkinliğin kaydı iptal edilemez.";
    internal const string CancelFailed = "Kayıt iptal edilemedi.";

    public async Task<RegistrationReceipt> RegisterAsync(
        UserSession session,
        int eventId,
        string? nationalId,
        CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);

        if (eventId <= 0)
        {
            throw new BusinessRuleException(EventRequired);
        }

        // Rows of the original application were checked for the format only, so the lookup does not
        // require a valid checksum; new participants are checked fully when they are added.
        var id = nationalId?.Trim();
        if (!NationalIdNumber.HasValidFormat(id))
        {
            throw new BusinessRuleException(NationalIdFormat);
        }

        var participant = await participants.FindByNationalIdAsync(id!, cancellationToken).ConfigureAwait(false);

        // Somebody else's participant gets the same answer as an unknown one, so national ids cannot be probed.
        if (participant is null || !(session.IsAdmin || participant.OwnerUserId == session.UserId))
        {
            throw new BusinessRuleException(ParticipantNotFound);
        }

        var target = await events.GetByIdAsync(eventId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException(EventRequired);
        var today = timeProvider.Today();
        if (target.Date < today)
        {
            throw new BusinessRuleException(PastEvent);
        }

        if (target.FreeSeats == 0)
        {
            throw new BusinessRuleException(EventFull);
        }

        var code = TicketCode.New();
        try
        {
            // The repository repeats the seat, date and duplicate checks inside its transaction; the
            // checks above only give the common cases a quick answer.
            var registrationId = await registrations.AddAsync(
                participant.Id,
                eventId,
                today,
                timeProvider.GetUtcNow().UtcDateTime,
                code,
                cancellationToken).ConfigureAwait(false);
            return new RegistrationReceipt(registrationId, code, target.Name, target.Date);
        }
        catch (AlreadyRegisteredException ex)
        {
            throw new BusinessRuleException(AlreadyRegistered, ex);
        }
        catch (EventFullException ex)
        {
            throw new BusinessRuleException(EventFull, ex);
        }
        catch (EventClosedException ex)
        {
            throw new BusinessRuleException(PastEvent, ex);
        }
    }

    public Task<IReadOnlyList<RegistrationDetails>> GetMyRegistrationsAsync(
        UserSession session,
        CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        return registrations.GetActiveAsync(Access.OwnerFilter(session), cancellationToken);
    }

    public async Task CancelAsync(UserSession session, int registrationId, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);

        var registration = await registrations.GetOwnershipAsync(registrationId, cancellationToken).ConfigureAwait(false);

        // A missing registration and somebody else's registration get the same answer, so ids cannot be probed.
        if (registration is null || !(session.IsAdmin || registration.OwnerUserId == session.UserId))
        {
            throw new AccessDeniedException();
        }

        if (registration.Status == RegistrationStatus.Cancelled)
        {
            throw new BusinessRuleException(AlreadyCancelled);
        }

        var today = timeProvider.Today();
        if (registration.EventDate < today)
        {
            throw new BusinessRuleException(PastCannotCancel);
        }

        // The repository repeats the ownership, status and date conditions in the same statement, so a
        // concurrent change between the read above and this update cannot slip through.
        var cancelled = await registrations.CancelAsync(
            registrationId,
            Access.OwnerFilter(session),
            today,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken).ConfigureAwait(false);
        if (!cancelled)
        {
            throw new BusinessRuleException(CancelFailed);
        }
    }
}

/// <summary>Random ticket codes such as "ETK-7KQ2M9XA4C".</summary>
internal static class TicketCode
{
    public const string Prefix = "ETK-";
    public const int RandomLength = 10;

    // No 0/O, 1/I: the code is read and typed by people.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New() => Prefix + RandomNumberGenerator.GetString(Alphabet, RandomLength);
}
