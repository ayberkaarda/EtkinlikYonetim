using System.Globalization;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>Formats dates the way the screens show them.</summary>
internal static class DisplayFormat
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", Turkish);
}

/// <summary>How an event appears in the lists: its name, then the date and the seats, and a pill for its state.</summary>
internal static class EventRows
{
    /// <summary>
    /// The row of <paramref name="summary"/>. The pill says whether seats are free, the event is full (new
    /// registrations wait) or cancelled; with <paramref name="today"/> an event that took place says so.
    /// </summary>
    public static ListRow Describe(EventSummary summary, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var details = string.Create(DisplayFormat.Turkish, $"{DisplayFormat.Date(summary.Date)} · {summary.RegisteredCount}/{summary.Capacity} kayıt");
        if (summary.WaitingCount > 0)
        {
            details += string.Create(DisplayFormat.Turkish, $" · {summary.WaitingCount} kişi bekliyor");
        }

        var (status, tone) = summary switch
        {
            { Cancelled: true } => ("İptal edildi", RowTone.Warning),
            _ when today is { } day && summary.Date < day => ("Geçti", RowTone.Neutral),
            { FreeSeats: 0 } => ("Dolu", RowTone.Warm),
            _ => (string.Create(DisplayFormat.Turkish, $"{summary.FreeSeats} boş yer"), RowTone.Accent),
        };
        return new ListRow(summary.Name, details, RowTone.Neutral, status, tone);
    }
}

/// <summary>A registration in "my registrations": event, date, participant and ticket code or waiting place.</summary>
internal sealed record RegistrationItem(RegistrationDetails Registration)
{
    public override string ToString() =>
        $"{Registration.EventName} - {DisplayFormat.Date(Registration.EventDate)} - "
        + $"{Registration.ParticipantName ?? "Ad Bilinmiyor"} - {State(Registration)}";

    /// <summary>
    /// The row: the event, then the date and the participant, and a pill that tells a seat with a ticket, a
    /// place on the waiting list and a cancelled event apart. The ticket code itself stays off the list.
    /// </summary>
    public ListRow Row()
    {
        var details = $"{DisplayFormat.Date(Registration.EventDate)} · {Registration.ParticipantName ?? "Ad Bilinmiyor"}";
        var (status, tone) = Registration.Status switch
        {
            RegistrationStatus.Waiting => (string.Create(DisplayFormat.Turkish, $"Bekleme listesi · {Registration.QueuePosition}. sıra"), RowTone.Warm),
            RegistrationStatus.EventCancelled => ("Etkinlik iptal edildi", RowTone.Warning),
            _ => (Registration.TicketCode is { Length: > 0 } ? "Bilet hazır" : "Kayıtlı", RowTone.Accent),
        };
        return new ListRow(Registration.EventName, details, RowTone.Neutral, status, tone);
    }

    private static string State(RegistrationDetails registration) => registration.Status switch
    {
        RegistrationStatus.Waiting => $"Bekleme listesi, sıra: {registration.QueuePosition}",
        RegistrationStatus.EventCancelled => "Etkinlik iptal edildi",
        _ => $"Bilet: {registration.TicketCode ?? "-"}",
    };
}

/// <summary>An account in the account management: name, role and state.</summary>
internal sealed record AccountItem(AccountSummary Account)
{
    public override string ToString() =>
        $"{Account.UserName} - {(Account.Role == UserRole.Admin ? "Yönetici" : "Kullanıcı")} - {(Account.Disabled ? "Devre dışı" : "Etkin")}";
}

/// <summary>A participant of an event with the national id masked and the state of the registration; no ticket code.</summary>
internal sealed record ParticipantItem(ParticipantView Participant)
{
    public override string ToString() =>
        $"{Participant.FullName} - {Participant.MaskedNationalId} - {Participant.Email} - {Participant.State}";
}
