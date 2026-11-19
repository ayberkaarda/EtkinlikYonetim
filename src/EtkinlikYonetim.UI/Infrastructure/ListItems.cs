using System.Globalization;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>Formats dates the way the screens show them.</summary>
internal static class DisplayFormat
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", Turkish);
}

/// <summary>A registration in "my registrations": event, date, participant and ticket code.</summary>
internal sealed record RegistrationItem(RegistrationDetails Registration)
{
    public override string ToString() =>
        $"{Registration.EventName} - {DisplayFormat.Date(Registration.EventDate)} - "
        + $"{Registration.ParticipantName ?? "Ad Bilinmiyor"} - Bilet: {Registration.TicketCode ?? "-"}";
}

/// <summary>A participant of an event, as the original list showed it, with the national id masked.</summary>
internal sealed record ParticipantItem(ParticipantView Participant)
{
    public override string ToString() =>
        $"{Participant.FullName} - {Participant.MaskedNationalId} - {Participant.Email} - {Participant.TicketCode}";
}
