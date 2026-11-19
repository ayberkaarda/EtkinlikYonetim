using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Shows a ticket of the signed-in account's own registration with its QR code, as often as needed.
    /// The registration comes from the account's own list, which the services filter by owner.
    /// </summary>
    public sealed class BiletForm : AppDialog
    {
        private readonly QrView picQr = new() { Name = nameof(picQr) };
        private readonly TextBox txtKod = new() { Name = nameof(txtKod) };

        public BiletForm(RegistrationDetails registration)
            : base("Bilet", "Girişte bu QR kodu okutun ya da bilet kodunu söyleyin.", Glyphs.QrCode, new Size(640, 480), new Size(560, 420), modal: true)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (registration.TicketCode is not { Length: > 0 } code)
            {
                throw new ArgumentException("The registration has no ticket.", nameof(registration));
            }

            txtKod.Text = code;
            picQr.Image = TicketQr.Render(code);
            BuildLayout(registration);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            picQr.Image?.Dispose();
            picQr.Image = null;
            base.OnFormClosed(e);
        }

        private void BuildLayout(RegistrationDetails registration)
        {
            Name = nameof(BiletForm);

            // The ticket: what and when, who, and the code on the left; the QR code on the stub.
            var eventName = new HeadingLabel
            {
                Name = "lblEtkinlik",
                Text = registration.EventName,
                Font = Theme.Fonts.Title,
                ForeColor = Theme.Current.Text,
                Margin = Padding.Empty,
            };
            var date = FormLayout.Text("lblTarih", DisplayFormat.Date(registration.EventDate));
            date.ForeColor = Theme.Current.TextSecondary;
            date.Margin = new Padding(0, Spacing.XS, 0, Spacing.L);
            var participantCaption = FormLayout.Hint("Katılımcı");
            participantCaption.Margin = Padding.Empty;
            var participant = FormLayout.Text("lblKatilimci", registration.ParticipantName ?? "Ad Bilinmiyor");
            participant.Font = Theme.Fonts.Subtitle;
            participant.Margin = new Padding(0, 0, 0, Spacing.L);
            var code = FormLayout.Field("Bilet kodu", FormLayout.CodeInput(txtKod));
            code.Margin = Padding.Empty;
            var info = FormLayout.Stack("ticketInfo", eventName, date, participantCaption, participant, code);

            var perforation = new PerforationLine { Name = "perforation", Dock = DockStyle.Fill };
            picQr.Anchor = AnchorStyles.Top;
            var ticket = new TableLayoutPanel
            {
                Name = "ticketLayout",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
            };
            ticket.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ticket.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ticket.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ticket.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ticket.Controls.Add(info, 0, 0);
            ticket.Controls.Add(perforation, 1, 0);
            ticket.Controls.Add(picQr, 2, 0);

            var card = new TicketPanel
            {
                Name = "ticketCard",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(Spacing.XL, Spacing.XL, Spacing.XL, Spacing.XL),
                Perforation = perforation,
                Edge = Theme.Current.Warm,
            };
            card.Controls.Add(ticket);

            var secret = FormLayout.Text("lblBilgi", "Bilet kodunu kimseyle paylaşmayın; kodu bilen bileti kullanabilir. Bilet girişte bir kez okutulur.");
            var note = FormLayout.Note("secretNote", Glyphs.Lock, secret);
            note.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Page("ticketPage", (card, false), (note, false)));

            SetFooterNote("Bileti istediğiniz kadar açabilirsiniz; kod değişmez.");
            SetFooterButtons(CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
