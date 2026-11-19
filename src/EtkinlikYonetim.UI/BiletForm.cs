using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Shows a ticket of the signed-in account's own registration with its QR code, as often as needed.
    /// The registration comes from the account's own list, which the services filter by owner.
    /// </summary>
    public partial class BiletForm : Form
    {
        public BiletForm(RegistrationDetails registration)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (registration.TicketCode is not { Length: > 0 } code)
            {
                throw new ArgumentException("The registration has no ticket.", nameof(registration));
            }

            InitializeComponent();
            Icon = AppAssets.Icon;
            Text = AppText.Title + " - " + Text;
            lblEtkinlik.Text = $"{registration.EventName} - {DisplayFormat.Date(registration.EventDate)}{Environment.NewLine}{registration.ParticipantName ?? "Ad Bilinmiyor"}";
            txtKod.Text = code;
            picQr.Image = TicketQr.Render(code);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            picQr.Image?.Dispose();
            picQr.Image = null;
            base.OnFormClosed(e);
        }
    }
}