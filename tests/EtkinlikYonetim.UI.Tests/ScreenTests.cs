using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Tests.Support;
using EtkinlikYonetim.UI.Theming;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>
/// Every screen is built with test doubles and laid out without being shown: window handles are created
/// for real layout and tab order, but no window becomes visible and no message window opens.
/// </summary>
public sealed class ScreenTests
{
    public static TheoryData<string> ScreenNames => [.. TestForms.Names];

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_HasTheApplicationIdentity(string name) => Run(() =>
    {
        using var form = TestForms.Create(name);

        Assert.StartsWith(AppText.Title + " - ", form.Text, StringComparison.Ordinal);
        Assert.Same(AppAssets.Icon, form.Icon);
        Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
        Assert.True(form.MinimumSize.Width > 0 && form.MinimumSize.Height > 0, "A resizable screen needs a minimum size.");
        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        Assert.Null(form.BackgroundImage);
        Assert.False(form.Visible);
    });

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_UsesThemedControlsAndAKeyboardWayOut(string name) => Run(() =>
    {
        using var form = TestForms.Create(name);
        var controls = Descendants(form).ToList();

        var buttons = controls.OfType<Button>().ToList();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.IsType<ThemedButton>(button));
        Assert.All(buttons, button => Assert.False(string.IsNullOrWhiteSpace(button.Text), $"{button.Name} has no text."));
        Assert.All(controls.OfType<ComboBox>(), combo => Assert.IsType<ThemedComboBox>(combo));
        Assert.All(controls.OfType<ListBox>(), list => Assert.IsType<ThemedListBox>(list));
        Assert.All(controls.OfType<NumericUpDown>(), number => Assert.IsType<NumberField>(number.Parent));
        Assert.All(controls.OfType<DateTimePicker>(), date => Assert.IsType<DateField>(date.Parent));
        Assert.All(controls.OfType<TextBox>().Where(box => box.Parent is not NumericUpDown), box => Assert.IsType<TextField>(box.Parent));
        Assert.All(controls.OfType<ThemedListBox>(), list => Assert.False(string.IsNullOrEmpty(list.EmptyText), $"{list.Name} has no empty text."));
        Assert.NotNull(form.CancelButton); // Esc closes every screen (a panel signs out).
    });

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_LaysOutInsideItsWindow(string name) => Run(() =>
    {
        using var form = TestForms.Create(name);

        LayoutAssert.FitsInside(form);
        Assert.False(form.Visible);
    });

    public static TheoryData<string, int, int, float> ScreensOnSmallScreens
    {
        get
        {
            var data = new TheoryData<string, int, int, float>();
            foreach (var (width, height) in new[] { (1024, 600), (1366, 768) })
            {
                foreach (var scale in LayoutAssert.FontScales)
                {
                    foreach (var name in TestForms.Names)
                    {
                        data.Add(name, width, height, scale);
                    }
                }
            }

            return data;
        }
    }

    /// <summary>
    /// On a small working area and with larger text the window still opens inside the screen, its minimum size
    /// too, and every control stays reachable: the body scrolls while the header and the footer stay in view,
    /// also while a two-line message is shown.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScreensOnSmallScreens))]
    public void Screen_FitsTheWorkingAreaAndKeepsEveryControlReachable(string name, int screenWidth, int screenHeight, float fontScale) => Run(() =>
    {
        var previous = Theme.Current;
        var previousScale = Theme.RenderScale;
        var previousArea = AppDialog.WorkingAreaOf;
        Theme.Use(previous, fontScale);
        var area = new Rectangle(0, 0, screenWidth, screenHeight);
        AppDialog.WorkingAreaOf = _ => area;
        try
        {
            using var form = TestForms.Create(name);
            LayoutAssert.CreateHandles(form);

            Assert.True(form.Width <= area.Width && form.Height <= area.Height, $"{name} opens at {form.Size}, larger than the working area {area.Size}.");
            Assert.True(form.MinimumSize.Width <= area.Width && form.MinimumSize.Height <= area.Height, $"{name} requires {form.MinimumSize}, larger than the working area {area.Size}.");

            foreach (var size in new[] { form.Size, form.MinimumSize })
            {
                form.Size = size;
                form.ShowStatus(StatusKind.Warning, "Lütfen bir etkinlik seçin." + Environment.NewLine + "İkinci satır.");
                form.PerformLayout();

                try
                {
                    LayoutAssert.FitsInside(form);
                }
                catch (Xunit.Sdk.XunitException ex)
                {
                    throw new Xunit.Sdk.XunitException($"{name} at {form.Size}: {ex.Message}");
                }

                form.ClearStatus();
            }
        }
        finally
        {
            AppDialog.WorkingAreaOf = previousArea;
            Theme.Use(previous, previousScale);
        }
    });

    [Theory]
    [InlineData(nameof(GirisYapForm), "txtKullaniciAdi", "btnCikis")]
    [InlineData(nameof(KayitOlForm), "txtKullaniciAdi", "btnKapat")]
    [InlineData(nameof(IlkKurulumForm), "txtAnahtar", "btnIptal")]
    [InlineData(nameof(Form1), "txtEtkinlikAd", "btnCikis")]
    [InlineData(nameof(KatilimciEkleForm), "txtTC", "btnCikis")]
    [InlineData(nameof(KayitEkleForm), "cmbEtkinlikler", "btnKapat")]
    [InlineData(nameof(BiletForm), "txtKod", "btnKapat")]
    [InlineData(nameof(BiletKontrolForm), "cmbEtkinlik", "btnKapat")]
    [InlineData(nameof(KatilimciGoruntule), "cmbEtkinlikSec", "btnKapat")]
    [InlineData(nameof(EtkinlikYonetForm), "lstEtkinlikler", "btnKapat")]
    [InlineData(nameof(KullaniciYonetForm), "lstHesaplar", "btnKapat")]
    public void TabOrder_StartsAtTheFirstInputAndEndsInTheFooter(string name, string first, string last) => Run(() =>
    {
        using var form = TestForms.Create(name);
        LayoutAssert.CreateHandles(form);

        var order = TabOrder(form);

        Assert.Equal(first, order[0].Name);
        Assert.Equal(last, order[^1].Name);
        Assert.Equal(order.Count, order.Distinct().Count());
    });

    [Theory]
    [InlineData(nameof(GirisYapForm), new[] { "txtKullaniciAdi", "txtSifre", "btnGiris", "btnKayitOl", "btnKurulum", "btnCikis" })]
    [InlineData(nameof(IlkKurulumForm), new[] { "txtAnahtar", "txtKullaniciAdi", "txtSifre", "txtSifreTekrar", "btnOlustur", "btnIptal" })]
    [InlineData(nameof(Form1), new[]
    {
        "txtEtkinlikAd", "dtpTarih", "numKapasite", "btnEtkinlikEkle", "btnEtkinlikYonet", "btnKatilimciEkle", "btnKayit",
        "btnBiletKontrol", "btnKatilimciGoruntule", "btnKullanicilar", "btnCikis",
    })]
    [InlineData(nameof(KayitEkleForm), new[] { "cmbEtkinlikler", "txtKatilimciTC", "btnKayit", "lstKayitlar", "btnBiletGoster", "btnKayitIptal", "btnKapat" })]
    [InlineData(nameof(BiletKontrolForm), new[] { "cmbEtkinlik", "txtKod", "btnKontrol", "btnKapat" })]
    [InlineData(nameof(EtkinlikYonetForm), new[] { "lstEtkinlikler", "dtpTarih", "numKapasite", "txtAciklama", "btnKaydet", "btnIptalEt", "btnKapat" })]
    public void TabOrder_FollowsTheReadingOrder(string name, string[] expected) => Run(() =>
    {
        using var form = TestForms.Create(name);
        LayoutAssert.CreateHandles(form);

        var names = TabOrder(form).Select(control => control.Name).Where(expected.Contains).ToList();

        Assert.Equal(expected, names);
    });

    [Fact]
    public void FieldCaptions_NameTheirInputs() => Run(() =>
    {
        using var form = TestForms.Create(nameof(GirisYapForm));

        Assert.Equal("Kullanıcı adı", Find<TextBox>(form, "txtKullaniciAdi").AccessibleName);
        Assert.Equal("Şifre", Find<TextBox>(form, "txtSifre").AccessibleName);
        Assert.False(Descendants(form).OfType<TextField>().Any(field => field.CanSelect), "A text field frame must hand the focus to its text box.");

        using var panel = TestForms.Create(nameof(Form1));
        Assert.Equal("Tarih", Find<DateTimePicker>(panel, "dtpTarih").AccessibleName);
    });

    [Fact]
    public void Validation_IsShownInlineWithoutADialog() => Run(() =>
    {
        using var form = TestForms.Registrations();
        var dialogs = NoDialogs.Requested.Count;
        form.Prompts = new ScreenPrompts(form);

        Assert.True(form.CancelAsync().IsCompletedSuccessfully);

        Assert.True(form.StatusBar.IsShown);
        Assert.Equal(StatusKind.Warning, form.StatusBar.Kind);
        Assert.Equal("Lütfen iptal etmek istediğiniz kaydı seçin.", form.StatusBar.Text);
        Assert.Equal(form.StatusBar.Text, form.StatusBar.AccessibleName);
        Assert.Equal(dialogs, NoDialogs.Requested.Count);
    });

    /// <summary>A refusal of the service (a business rule) appears in the screen's message strip, not in a window.</summary>
    [Fact]
    public void RuleRefusal_IsShownInline() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new GirisYapForm(auth, new NoFormsFactory());
        var dialogs = NoDialogs.Requested.Count;

        var signIn = form.SignInAsync();
        auth.RefuseSignIn();

        Assert.True(signIn.IsCompletedSuccessfully);
        Assert.Equal((StatusKind.Warning, "Kullanıcı adı veya şifre hatalı."), (form.StatusBar.Kind, form.StatusBar.Text));
        Assert.Equal(dialogs, NoDialogs.Requested.Count);
    });

    [Fact]
    public void NextWrite_ClearsThePreviousMessage() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new GirisYapForm(auth, new NoFormsFactory());
        var first = form.SignInAsync();
        auth.RefuseSignIn();
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(form.StatusBar.IsShown);

        _ = form.SignInAsync(); // Never completes; the old warning must be gone meanwhile.

        Assert.Equal(2, auth.SignInCalls);
        Assert.False(form.StatusBar.IsShown);
    });

    [Fact]
    public void CancelQuestion_GoesThroughTheDialogHelper() => Run(() =>
    {
        using var form = TestForms.Registrations();
        form.Prompts = new ScreenPrompts(form);
        Find<ListBox>(form, "lstKayitlar").SelectedIndex = 0;
        var before = NoDialogs.Requested.Count;

        Assert.True(form.CancelAsync().IsCompletedSuccessfully);

        Assert.Equal("Onay: Seçilen kaydı iptal etmek istediğinize emin misiniz?", Assert.Single(NoDialogs.Requested.Skip(before)));
    });

    /// <summary>A list says while it loads, and says why it is empty: no event chosen, or no participants.</summary>
    [Fact]
    public void Lists_SayWhenTheyLoadAndWhenTheyAreEmpty() => Run(() =>
    {
        var participants = new ControlledParticipantService();
        using var form = new KatilimciGoruntule(TestForms.Events(), participants, new ControlledExportService(), Sessions.Admin);
        var list = Find<ThemedListBox>(form, "lstKatilimcilar");
        Assert.Equal(KatilimciGoruntule.ChooseAnEvent, list.EmptyText);

        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Assert.True(list.IsLoading);
        participants.ListRequests[0].Answer.SetResult([]);

        Assert.False(list.IsLoading);
        Assert.Empty(list.Items);
        Assert.Equal(KatilimciGoruntule.NoParticipants, list.EmptyText);
    });

    /// <summary>
    /// The entrance shows its answer in full, also with a very long name, on a panel of the answer's kind;
    /// choosing another event clears it back to the waiting text.
    /// </summary>
    [Fact]
    public void CheckIn_ShowsTheWholeAnswerOnAPanelOfItsKind() => Run(() =>
    {
        using var form = TestForms.CheckIn();
        LayoutAssert.CreateHandles(form);
        var answer = Find<Label>(form, "lblSonuc");

        Assert.Equal(BiletKontrolForm.CheckedInText + " - " + TestForms.LongName, answer.Text);
        Assert.Equal(StatusKind.Success, form.ResultKind);
        Assert.False(FormLayout.IsHidden(answer));
        Assert.True(answer.Height > answer.Font.Height, "A long name wraps instead of running out of the panel.");
        Assert.True(FormLayout.IsHidden(Find<Label>(form, "lblBekleme")));
    });

    [Fact]
    public void MessageDialog_AnswersWithKeysAndIsNeverShown() => Run(() =>
    {
        using var dialog = new MessageDialog("Seçilen etkinlik iptal edilsin mi?", "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        Assert.False(dialog.Visible);
        Assert.Equal(DialogResult.Yes, Assert.IsType<ThemedButton>(dialog.AcceptButton).DialogResult);
        Assert.Equal(DialogResult.No, Assert.IsType<ThemedButton>(dialog.CancelButton).DialogResult);
        Assert.Equal(AppText.Title, dialog.Text);
        Assert.True(dialog.ClientSize.Width > 0 && dialog.ClientSize.Height > 0);
        Assert.Equal("Bir sorun oluştu", MessageDialog.Heading(AppText.Title, MessageBoxIcon.Error));
        Assert.Equal("Uyarı", MessageDialog.Heading("Uyarı", MessageBoxIcon.Warning));
    });

    /// <summary>The sign-in screen says why the previous session ended, or that the first admin was created.</summary>
    [Fact]
    public void SignIn_ShowsANotice() => Run(() =>
    {
        using var form = new GirisYapForm(new PendingAuthService(), new NoFormsFactory());

        form.ShowNotice(GirisYapForm.AdminCreatedNotice);

        Assert.Equal((StatusKind.Info, GirisYapForm.AdminCreatedNotice), (form.StatusBar.Kind, form.StatusBar.Text));
    });

    internal static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Focusable controls in the order Tab visits them.</summary>
    private static List<Control> TabOrder(Form form)
    {
        var order = new List<Control>();
        Control? current = null;
        while ((current = form.GetNextControl(current, forward: true)) is not null)
        {
            if (CanSelectWhenShown(current))
            {
                order.Add(current);
            }
        }

        return order;
    }

    /// <summary>
    /// Whether Tab can stop on the control once the window is shown: an enabled tab stop that is not a
    /// container, label, frame or badge, not hidden by its screen and not the inner box of a number field.
    /// </summary>
    private static bool CanSelectWhenShown(Control control) =>
        control.TabStop
        && control.Enabled
        && !FormLayout.IsHidden(control)
        && control.Parent is not NumericUpDown
        && control is not (Label or Panel or TableLayoutPanel or FlowLayoutPanel or TextField or NumberField or DateField or GlyphBadge or InfoBar or BrandRule or PerforationLine or PictureBox);
}
