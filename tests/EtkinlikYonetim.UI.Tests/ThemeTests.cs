using Microsoft.Extensions.Configuration;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Tests.Support;
using EtkinlikYonetim.UI.Theming;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>The palettes and tokens stay readable and consistent; high contrast uses the system colors, also when it is switched on while screens are open.</summary>
public sealed class ThemeTests
{
    public static TheoryData<string> Palettes => ["Light", "Dark"];

    [Theory]
    [MemberData(nameof(Palettes))]
    public void TextRoles_MeetWcagContrast(string name)
    {
        var p = Palette(name);

        AssertContrast(p.Text, p.Surface, 7);
        AssertContrast(p.Text, p.Page, 7);
        AssertContrast(p.Text, p.SurfaceAlt, 7);
        AssertContrast(p.Text, p.AccentSubtle, 7);
        AssertContrast(p.TextSecondary, p.Surface, 4.5);
        AssertContrast(p.TextSecondary, p.SurfaceAlt, 4.5);
        AssertContrast(p.TextSecondary, p.AccentSubtle, 4.5);
        AssertContrast(p.TextMuted, p.Surface, 4.5);
        AssertContrast(p.TextMuted, p.Page, 4.5);
        AssertContrast(p.TextMuted, p.SurfaceAlt, 4.5);
        AssertContrast(p.OnAccent, p.Accent, 4.5);
        AssertContrast(p.OnAccent, p.AccentHover, 4.5);
        AssertContrast(p.OnAccent, p.AccentPressed, 4.5);
        AssertContrast(p.OnAccentMuted, p.Accent, 4.5);
        AssertContrast(p.AccentText, p.Surface, 4.5);
        AssertContrast(p.AccentText, p.AccentSubtle, 4.5);
        AssertContrast(p.WarmText, p.WarmSubtle, 4.5);
        AssertContrast(p.Text, p.WarmSubtle, 7);
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void StatusColors_AreReadableInMessagesAndPills(string name)
    {
        var p = Palette(name);

        foreach (var (accent, fill) in new[] { (p.Success, p.SuccessSubtle), (p.Warning, p.WarningSubtle), (p.Danger, p.DangerSubtle), (p.Info, p.InfoSubtle) })
        {
            AssertContrast(p.Text, fill, 7);
            AssertContrast(accent, fill, 4.5);
        }

        // Every status pill, on its own fill, on a plain and on a selected row.
        foreach (var tone in Enum.GetValues<RowTone>())
        {
            foreach (var selected in new[] { false, true })
            {
                var (fill, _, text) = ThemedListBox.PillColors(p, selected, tone);
                AssertContrast(text, fill, 4.5);
            }
        }

        // Second lines of list rows on plain and selected rows.
        foreach (var row in new[] { p.Surface, p.AccentSubtle })
        {
            AssertContrast(p.TextSecondary, row, 4.5);
            AssertContrast(p.AccentText, row, 4.5);
            AssertContrast(p.Warning, row, 4.5);
            AssertContrast(p.Danger, row, 4.5);
        }
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void BordersAndAccent_AreDistinguishable(string name)
    {
        var p = Palette(name);

        AssertContrast(p.BorderStrong, p.Surface, 3);
        AssertContrast(p.Accent, p.Surface, 3);
        AssertContrast(p.Accent, p.Page, 3);

        // The primary (accent) and destructive (danger) actions must not look alike.
        Assert.True(HueDistance(p.Accent, p.Danger) >= 90, $"Accent and danger hues are only {HueDistance(p.Accent, p.Danger):0} degrees apart.");
    }

    [Fact]
    public void Palettes_DefineEveryRole()
    {
        foreach (var palette in new[] { ThemePalette.Light, ThemePalette.Dark, ThemePalette.HighContrast() })
        {
            var colors = typeof(ThemePalette).GetProperties()
                .Where(property => property.PropertyType == typeof(Color))
                .Select(property => (property.Name, Value: (Color)property.GetValue(palette)!))
                .ToList();

            Assert.NotEmpty(colors);
            Assert.All(colors, color => Assert.False(color.Value.IsEmpty, $"{palette.Name}.{color.Name} is not set."));
        }
    }

    /// <summary>
    /// The identity is a cobalt blue with a magenta spotlight, in both palettes; the magenta stays clear of the red
    /// of a ticket seller's brand and of the danger role.
    /// </summary>
    [Fact]
    public void Light_AndDark_UseTheSameCobaltAndMagenta()
    {
        Assert.InRange(HueDistance(ThemePalette.Light.Accent, ThemePalette.Dark.Accent), 0, 12);
        Assert.InRange(ThemePalette.Light.Accent.GetHue(), 215, 232);
        Assert.InRange(ThemePalette.Light.Warm.GetHue(), 318, 340);
        Assert.InRange(ThemePalette.Dark.Warm.GetHue(), 318, 340);
        Assert.True(HueDistance(ThemePalette.Light.Warm, ThemePalette.Light.Danger) >= 25, "The magenta mark must not read as the danger red.");
        Assert.True(ThemePalette.Dark.IsDark);
        Assert.False(ThemePalette.Light.IsDark);
    }

    /// <summary>Headings use Bahnschrift where it is installed and fall back to Segoe UI Semibold, never to a substitute face.</summary>
    [Fact]
    public void HeadingFont_FallsBackToSegoe()
    {
        Assert.Equal(Typography.SemiboldFamily, Theme.FamilyOf(Typography.Title with { Family = "Yok Böyle Bir Yazı Tipi" }));
        Assert.Equal(Typography.Family, Theme.FamilyOf(Typography.Body));
        Assert.Contains(Theme.FamilyOf(Typography.Title), new[] { Typography.HeadingFamily, Typography.SemiboldFamily });
    }

    [Fact]
    public void HighContrast_UsesTheSystemColors()
    {
        var p = ThemePalette.HighContrast();

        Assert.True(p.IsHighContrast);
        Assert.Equal(SystemColors.WindowText, p.Text);
        Assert.Equal(SystemColors.Window, p.Surface);
        Assert.Equal(SystemColors.Highlight, p.Accent);
        Assert.Equal(SystemColors.HighlightText, p.OnAccent);
    }

    /// <summary>
    /// A valid high contrast scheme whose text color equals the highlight color: palette colors on a selected
    /// row would vanish, so selected rows and their pills use HighlightText.
    /// </summary>
    [Fact]
    public void HighContrast_SelectedContentUsesHighlightText()
    {
        var highlight = Color.Navy;
        var highlightText = Color.Yellow;
        var palette = ThemePalette.HighContrast() with
        {
            Text = highlight,
            TextSecondary = highlight,
            AccentText = highlight,
            WarmText = highlight,
            Warning = highlight,
            Accent = highlight,
            OnAccent = highlightText,
        };

        foreach (var tone in Enum.GetValues<RowTone>())
        {
            Assert.Equal((highlight, highlightText, highlightText), ThemedListBox.RowColors(palette, selected: true, tone));
            Assert.Equal(highlight, ThemedListBox.RowColors(palette, selected: false, tone).Primary);
            var pill = ThemedListBox.PillColors(palette, selected: true, tone);
            Assert.Equal((highlight, highlightText, highlightText), pill);
        }
    }

    [Fact]
    public void LightRows_KeepThePaletteText()
    {
        var p = ThemePalette.Light;

        Assert.Equal((p.AccentSubtle, p.Text, p.TextSecondary), ThemedListBox.RowColors(p, selected: true, RowTone.Neutral));
        Assert.Equal(p.Warning, ThemedListBox.RowColors(p, selected: false, RowTone.Warning).Secondary);
        Assert.Equal((p.WarmSubtle, p.Warm, p.WarmText), ThemedListBox.PillColors(p, selected: false, RowTone.Warm));
    }

    [Fact]
    public void Tokens_AreOrderedScales()
    {
        Assert.Equal(Spacing.Scale.Order(), Spacing.Scale);
        Assert.Equal(Spacing.Scale.Count, Spacing.Scale.Distinct().Count());
        Assert.All(Spacing.Scale, step => Assert.Equal(0, step % 4));

        var sizes = Typography.Ramp.Select(token => token.Size).ToList();
        Assert.Equal(sizes.Order(), sizes);
        Assert.Equal(sizes.Count, sizes.Distinct().Count());
        Assert.True(Typography.Body.Size >= 9F, "Body text must not be smaller than 9 pt.");
        Assert.True(Typography.Caption.Size >= 9F, "Captions must not be smaller than 9 pt.");
        Assert.True(Typography.Code.Size > Typography.Subtitle.Size, "A single use code must read larger than a section title.");
        Assert.True(Radius.Control <= Radius.Card);
        Assert.True(Sizes.ControlHeight >= 24, "Click targets need at least 24 logical pixels.");
        Assert.True(Sizes.ButtonMinWidth >= Sizes.ControlHeight);
    }

    [Fact]
    public void Glyphs_ArePrivateUseCharacters()
    {
        var glyphs = typeof(Glyphs).GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (field.Name, Value: (string)field.GetRawConstantValue()!))
            .ToList();

        Assert.NotEmpty(glyphs);
        Assert.All(glyphs, glyph => Assert.True(
            glyph.Value.Length == 1 && glyph.Value[0] is >= '' and <= '',
            $"{glyph.Name} is not a single icon font character."));
    }

    [Theory]
    [InlineData(ThemeMode.Light, false, false, "Light")]
    [InlineData(ThemeMode.Light, false, true, "Light")]
    [InlineData(ThemeMode.Dark, false, false, "Dark")]
    [InlineData(ThemeMode.System, false, true, "Dark")]
    [InlineData(ThemeMode.System, false, false, "Light")]
    [InlineData(ThemeMode.Dark, true, true, "HighContrast")]
    [InlineData(ThemeMode.Light, true, false, "HighContrast")]
    public void Resolve_HighContrastWinsThenTheSetting(ThemeMode mode, bool highContrast, bool systemDark, string expected)
    {
        var palette = Theme.Resolve(mode, highContrast, systemDark);

        Assert.Equal(expected, palette.Name);
        Assert.Equal(highContrast, palette.IsHighContrast);
    }

    [Theory]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData("System", ThemeMode.System)]
    [InlineData("LIGHT", ThemeMode.Light)]
    [InlineData(null, ThemeMode.Light)]
    [InlineData("", ThemeMode.Light)]
    [InlineData("neon", ThemeMode.Light)]
    [InlineData("1", ThemeMode.Light)]
    public void ThemeSetting_FallsBackToLight(string? value, ThemeMode expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AppConfiguration.ThemeKey] = value })
            .Build();

        Assert.Equal(expected, AppConfiguration.ResolveTheme(configuration));
    }

    [Fact]
    public void ContrastRatio_MatchesWcagReferenceValues()
    {
        Assert.Equal(21, Theme.ContrastRatio(Color.Black, Color.White), 3);
        Assert.Equal(1, Theme.ContrastRatio(Color.White, Color.White), 3);
        Assert.Equal(Theme.ContrastRatio(Color.Navy, Color.White), Theme.ContrastRatio(Color.White, Color.Navy), 9);
    }

    [Fact]
    public void HighContrastScreens_UseNativeControlsAndTheSystemColors() => Run(() =>
    {
        var previous = Theme.Current;
        var previousScale = Theme.RenderScale;
        Theme.Use(ThemePalette.HighContrast());
        try
        {
            using var form = new GirisYapForm(new PendingAuthService(), new NoFormsFactory());
            using var panel = new Form1(new ControlledEventService(), new NoFormsFactory(), Sessions.Admin);
            _ = panel.Handle;

            Assert.Equal(FlatStyle.System, Find<Button>(form, "btnGiris").FlatStyle);
            Assert.Equal(BorderStyle.FixedSingle, Find<TextBox>(form, "txtKullaniciAdi").BorderStyle);
            Assert.Equal(SystemColors.Window, Find<Control>(form, "brandPanel").BackColor);
            Assert.Null(Find<DateTimePicker>(panel, "dtpTarih").Region); // the native border stays
        }
        finally
        {
            Theme.Use(previous, previousScale);
        }
    });

    /// <summary>
    /// Windows switches high contrast on while the panels are open: lists, buttons, drop-down lists, text and
    /// number fields and check boxes go native with the system colors, and come back when it is switched off.
    /// </summary>
    [Fact]
    public void OpenScreens_RefreshWhenHighContrastChanges() => Run(() =>
    {
        var previous = Theme.Current;
        var previousMode = Theme.Mode;
        Theme.Initialize(ThemeMode.Light, highContrast: false, systemPrefersDark: false);
        var highContrast = false;
        var source = new ManualPreferenceSource();
        var watcher = new ThemeWatcher(source, ui: null, () => highContrast, () => false);
        try
        {
            using var panel = TestForms.AdminPanel();
            using var editor = TestForms.Registrations();
            _ = panel.Handle;
            _ = editor.Handle;
            var list = Find<ThemedListBox>(panel, "lstOzet");
            var button = Find<ThemedButton>(panel, "btnEtkinlikEkle");
            var combo = Find<ThemedComboBox>(editor, "cmbEtkinlikler");
            var box = Find<TextBox>(panel, "txtEtkinlikAd");
            var date = Find<DateTimePicker>(panel, "dtpTarih");
            var number = Find<NumericUpDown>(panel, "numKapasite");
            Assert.NotNull(date.Region);
            Assert.Equal(ThemePalette.Light.Surface, list.BackColor);
            Assert.Equal(FlatStyle.Flat, button.FlatStyle);
            Assert.Equal(DrawMode.OwnerDrawFixed, combo.DrawMode);
            Assert.Equal(BorderStyle.None, number.BorderStyle);

            highContrast = true;
            source.Raise();

            Assert.True(Theme.Current.IsHighContrast);
            Assert.Equal(SystemColors.Window, list.BackColor);
            Assert.Equal(SystemColors.WindowText, list.ForeColor);
            Assert.Equal(SystemColors.Window, panel.BackColor);
            Assert.Equal(SystemColors.Window, editor.BackColor);
            Assert.Equal(FlatStyle.System, button.FlatStyle);
            Assert.Equal(DrawMode.Normal, combo.DrawMode);
            Assert.Equal(BorderStyle.FixedSingle, box.BorderStyle);
            Assert.Equal(BorderStyle.FixedSingle, number.BorderStyle);
            Assert.Null(date.Region);
            var (back, primary, _) = ThemedListBox.RowColors(Theme.Current, selected: true, RowTone.Neutral);
            Assert.Equal((SystemColors.Highlight, SystemColors.HighlightText), (back, primary));

            highContrast = false;
            source.Raise();

            Assert.False(Theme.Current.IsHighContrast);
            Assert.Equal(ThemePalette.Light.Surface, list.BackColor);
            Assert.Equal(ThemePalette.Light.Page, panel.BackColor);
            Assert.Equal(FlatStyle.Flat, button.FlatStyle);
            Assert.Equal(DrawMode.OwnerDrawFixed, combo.DrawMode);
            Assert.Equal(BorderStyle.None, box.BorderStyle);
            Assert.NotNull(date.Region);

            // Ending the subscription ends the refresh.
            watcher.Dispose();
            highContrast = true;
            source.Raise();
            Assert.False(Theme.Current.IsHighContrast);
        }
        finally
        {
            watcher.Dispose();
            Theme.Initialize(previousMode, highContrast: false, systemPrefersDark: false);
            Theme.Use(previous);
        }
    });

    private sealed class ManualPreferenceSource : IPreferenceSource
    {
        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private static ThemePalette Palette(string name) => name == "Dark" ? ThemePalette.Dark : ThemePalette.Light;

    private static double HueDistance(Color first, Color second)
    {
        var distance = Math.Abs(first.GetHue() - second.GetHue());
        return Math.Min(distance, 360 - distance);
    }

    private static void AssertContrast(Color foreground, Color background, double minimum)
    {
        var ratio = Theme.ContrastRatio(foreground, background);
        Assert.True(ratio >= minimum, $"Contrast of {Hex(foreground)} on {Hex(background)} is {ratio:0.00}, expected at least {minimum}.");
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
