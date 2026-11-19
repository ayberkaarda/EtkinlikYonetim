using System.Globalization;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using FluentValidation;

namespace EtkinlikYonetim.Business.Services;

/// <summary>
/// A participant of an event as shown to admins: the national id is masked, the state tells the seat
/// and its check-in, the waiting place or the cancellation. The ticket code is never part of it: it is the
/// participant's secret, shown only to the account that owns the registration.
/// </summary>
public sealed record ParticipantView(string FullName, string MaskedNationalId, string Email, string State);

/// <summary>Adding participants and listing the participants of an event.</summary>
public interface IParticipantService
{
    /// <summary>
    /// Adds a participant owned by the signed-in account; only that account (and admins) can register
    /// it for events. Returns the new participant id.
    /// </summary>
    Task<int> AddAsync(
        UserSession session,
        string? nationalId,
        string? fullName,
        string? email,
        CancellationToken cancellationToken = default);

    /// <summary>Participants registered for the event, with masked national ids; admins only.</summary>
    Task<IReadOnlyList<ParticipantView>> GetByEventAsync(UserSession session, int eventId, CancellationToken cancellationToken = default);
}

internal sealed class ParticipantService(
    IParticipantRepository participants,
    IValidator<ParticipantInput> validator,
    ISessionGuard sessions,
    IAuditLog audit) : IParticipantService
{
    internal const string FieldsRequired = "Lütfen tüm alanları doldurun.";
    internal const string NationalIdInvalid = "Lütfen geçerli bir T.C. Kimlik No girin.";
    internal const string FullNameInvalid = "Ad soyad yalnızca harf ve tek boşluk içerebilir ve en fazla 64 karakter olabilir.";
    internal const string EmailInvalid = "Geçersiz e-posta adresi!";
    internal const string NationalIdTaken = "Bu T.C. Kimlik No ile listenizde kayıtlı bir katılımcı zaten var.";

    internal const string UnknownName = "Ad Bilinmiyor";
    internal const string UnknownEmail = "E-posta Yok";
    internal const string CheckedIn = "Giriş yapıldı";
    internal const string NotCheckedIn = "Giriş yapılmadı";
    internal const string WaitingPrefix = "Bekleme listesi, sıra: ";
    internal const string EventCancelled = "Etkinlik iptal edildi";

    public async Task<int> AddAsync(
        UserSession session,
        string? nationalId,
        string? fullName,
        string? email,
        CancellationToken cancellationToken = default)
    {
        await sessions.RequireCurrentAsync(session, cancellationToken).ConfigureAwait(false);

        var input = ParticipantInput.Create(nationalId, fullName, email);
        validator.ValidateOrThrow(input);
        var (id, name, mail) = (input.NationalId, input.FullName, input.Email);

        // Only the account's own list is checked: national ids are unique per owner, so the answer for
        // a number another account has is the same as for a number nobody has.
        if ((await participants.FindByNationalIdAsync(id, session.UserId, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            throw new BusinessRuleException(NationalIdTaken);
        }

        var participant = new Participant
        {
            NationalId = id,
            FullName = name,
            Email = mail,
            OwnerUserId = session.UserId,
        };

        int added;
        try
        {
            added = await participants.AddAsync(participant, cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateNationalIdException ex)
        {
            // Added to the same account concurrently between the check and the insert.
            throw new BusinessRuleException(NationalIdTaken, ex);
        }

        audit.Record(AuditEvent.ParticipantAdded, session.Role);
        return added;
    }

    public async Task<IReadOnlyList<ParticipantView>> GetByEventAsync(
        UserSession session,
        int eventId,
        CancellationToken cancellationToken = default)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);

        var rows = await participants.GetByEventAsync(eventId, cancellationToken).ConfigureAwait(false);
        return rows.Select(View).ToList();
    }

    /// <summary>The admin's view of a registration: masked national id, filled in gaps and the state; never the ticket code.</summary>
    internal static ParticipantView View(EventParticipant p) =>
        new(
            string.IsNullOrWhiteSpace(p.FullName) ? UnknownName : p.FullName,
            NationalIdNumber.Mask(p.NationalId),
            string.IsNullOrWhiteSpace(p.Email) ? UnknownEmail : p.Email,
            p.Status switch
            {
                RegistrationStatus.Waiting => string.Create(CultureInfo.InvariantCulture, $"{WaitingPrefix}{p.QueuePosition}"),
                RegistrationStatus.EventCancelled => EventCancelled,
                _ => p.CheckedInAt is null ? NotCheckedIn : CheckedIn,
            });
}
