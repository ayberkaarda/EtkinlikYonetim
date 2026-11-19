using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Services;

/// <summary>A participant of an event as shown to admins; the national id is masked.</summary>
public sealed record ParticipantView(string FullName, string MaskedNationalId, string Email, string TicketCode);

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

internal sealed class ParticipantService(IParticipantRepository participants) : IParticipantService
{
    internal const string FieldsRequired = "Lütfen tüm alanları doldurun.";
    internal const string NationalIdInvalid = "Lütfen geçerli bir T.C. Kimlik No girin.";
    internal const string FullNameInvalid = "Ad soyad yalnızca harf ve tek boşluk içerebilir ve en fazla 64 karakter olabilir.";
    internal const string EmailInvalid = "Geçersiz e-posta adresi!";
    internal const string NationalIdTaken = "Bu T.C. Kimlik No ile listenizde kayıtlı bir katılımcı zaten var.";

    internal const string UnknownName = "Ad Bilinmiyor";
    internal const string UnknownEmail = "E-posta Yok";
    internal const string NoTicket = "Bilet Yok";

    public async Task<int> AddAsync(
        UserSession session,
        string? nationalId,
        string? fullName,
        string? email,
        CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);

        var id = nationalId?.Trim();
        var name = fullName?.Trim();
        var mail = email?.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(mail))
        {
            throw new BusinessRuleException(FieldsRequired);
        }

        if (!NationalIdNumber.IsValid(id))
        {
            throw new BusinessRuleException(NationalIdInvalid);
        }

        if (!InputRules.IsValidPersonName(name))
        {
            throw new BusinessRuleException(FullNameInvalid);
        }

        if (!InputRules.IsValidEmail(mail))
        {
            throw new BusinessRuleException(EmailInvalid);
        }

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

        try
        {
            return await participants.AddAsync(participant, cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateNationalIdException ex)
        {
            // Added to the same account concurrently between the check and the insert.
            throw new BusinessRuleException(NationalIdTaken, ex);
        }
    }

    public async Task<IReadOnlyList<ParticipantView>> GetByEventAsync(
        UserSession session,
        int eventId,
        CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session);

        var rows = await participants.GetByEventAsync(eventId, cancellationToken).ConfigureAwait(false);
        return rows
            .Select(p => new ParticipantView(
                string.IsNullOrWhiteSpace(p.FullName) ? UnknownName : p.FullName,
                NationalIdNumber.Mask(p.NationalId),
                string.IsNullOrWhiteSpace(p.Email) ? UnknownEmail : p.Email,
                p.TicketCode ?? NoTicket))
            .ToList();
    }
}
