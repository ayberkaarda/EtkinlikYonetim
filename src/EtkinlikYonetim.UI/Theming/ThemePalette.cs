namespace EtkinlikYonetim.UI.Theming;

/// <summary>
/// Named colors of one theme. Controls never use literal colors; they read a role from the current
/// palette, so the light, dark and high contrast themes stay consistent.
/// </summary>
internal sealed record ThemePalette
{
    public required string Name { get; init; }

    public bool IsDark { get; init; }

    /// <summary>Built from the Windows high contrast colors; custom painting steps back for native drawing.</summary>
    public bool IsHighContrast { get; init; }

    /// <summary>Window background behind cards: a cool grey with a faint blue tint, like a hall before the lights go down.</summary>
    public required Color Page { get; init; }

    /// <summary>Cards, inputs, header and footer.</summary>
    public required Color Surface { get; init; }

    /// <summary>Quiet areas: disabled inputs and the footer of the message dialog.</summary>
    public required Color SurfaceAlt { get; init; }

    public required Color SurfaceHover { get; init; }

    /// <summary>Decorative hairlines between areas.</summary>
    public required Color Border { get; init; }

    /// <summary>Borders of inputs; at least 3:1 against the surface.</summary>
    public required Color BorderStrong { get; init; }

    public required Color Text { get; init; }

    public required Color TextSecondary { get; init; }

    public required Color TextMuted { get; init; }

    public required Color TextDisabled { get; init; }

    /// <summary>The cobalt blue of the logo's tile: primary actions, selection and the brand panel.</summary>
    public required Color Accent { get; init; }

    public required Color AccentHover { get; init; }

    public required Color AccentPressed { get; init; }

    /// <summary>Text and glyphs on <see cref="Accent"/>.</summary>
    public required Color OnAccent { get; init; }

    /// <summary>Secondary text on <see cref="Accent"/>, for example the tagline of the brand panel.</summary>
    public required Color OnAccentMuted { get; init; }

    /// <summary>Tinted background for selected rows and glyph badges.</summary>
    public required Color AccentSubtle { get; init; }

    /// <summary>Accent used as text or a glyph on <see cref="Surface"/>.</summary>
    public required Color AccentText { get; init; }

    /// <summary>
    /// The magenta of the logo's ticket stub, a spotlight colour: a small mark for what waits (a place on the
    /// waiting list) and the brand line. Decorative only; text in this family uses <see cref="WarmText"/>.
    /// </summary>
    public required Color Warm { get; init; }

    public required Color WarmSubtle { get; init; }

    public required Color WarmText { get; init; }

    public required Color Success { get; init; }

    public required Color SuccessSubtle { get; init; }

    public required Color Warning { get; init; }

    public required Color WarningSubtle { get; init; }

    public required Color Danger { get; init; }

    public required Color DangerSubtle { get; init; }

    public required Color Info { get; init; }

    public required Color InfoSubtle { get; init; }

    /// <summary>
    /// Light theme: cool ink-grey neutrals with a cobalt accent and a magenta spotlight mark. The accent is far
    /// from the red danger role, so cancelling an event or a registration never looks like the primary action,
    /// and neither colour is the red of a ticket seller's brand.
    /// </summary>
    public static ThemePalette Light { get; } = new()
    {
        Name = "Light",
        Page = Hex(0xF2F4F8),
        Surface = Hex(0xFFFFFF),
        SurfaceAlt = Hex(0xF6F7FB),
        SurfaceHover = Hex(0xEDF1FB),
        Border = Hex(0xDDE2EC),
        BorderStrong = Hex(0x7A8399),
        Text = Hex(0x141A2B),
        TextSecondary = Hex(0x3F475C),
        TextMuted = Hex(0x566078),
        TextDisabled = Hex(0xA3AABA),
        Accent = Hex(0x1F56D6),
        AccentHover = Hex(0x1A49B8),
        AccentPressed = Hex(0x163D9A),
        OnAccent = Hex(0xFFFFFF),
        OnAccentMuted = Hex(0xD6E1FA),
        AccentSubtle = Hex(0xE8EEFC),
        AccentText = Hex(0x1A4CC2),
        Warm = Hex(0xD6338A),
        WarmSubtle = Hex(0xFCE8F2),
        WarmText = Hex(0xA11D63),
        Success = Hex(0x1B6E45),
        SuccessSubtle = Hex(0xE5F4EC),
        Warning = Hex(0x7A4A00),
        WarningSubtle = Hex(0xFBF0DA),
        Danger = Hex(0xB42318),
        DangerSubtle = Hex(0xFCEAE8),
        Info = Hex(0x2B4E8C),
        InfoSubtle = Hex(0xE8EEF8),
    };

    /// <summary>Dark theme: the same roles stepped for a dark ink-blue surface, not an inverted light theme.</summary>
    public static ThemePalette Dark { get; } = new()
    {
        Name = "Dark",
        IsDark = true,
        Page = Hex(0x0E121C),
        Surface = Hex(0x161B28),
        SurfaceAlt = Hex(0x1B2131),
        SurfaceHover = Hex(0x222A3D),
        Border = Hex(0x2C3448),
        BorderStrong = Hex(0x75809A),
        Text = Hex(0xE9EDF6),
        TextSecondary = Hex(0xBCC4D6),
        TextMuted = Hex(0x9AA3B8),
        TextDisabled = Hex(0x5A6378),
        Accent = Hex(0x7FA6FF),
        AccentHover = Hex(0x93B4FF),
        AccentPressed = Hex(0x6C97F5),
        OnAccent = Hex(0x0B1633),
        OnAccentMuted = Hex(0x1C2C57),
        AccentSubtle = Hex(0x1E2A4A),
        AccentText = Hex(0x8FB0FF),
        Warm = Hex(0xF06AAE),
        WarmSubtle = Hex(0x3A1A2D),
        WarmText = Hex(0xF7A3CC),
        Success = Hex(0x7FD3A0),
        SuccessSubtle = Hex(0x14302A),
        Warning = Hex(0xF0BF5C),
        WarningSubtle = Hex(0x33290F),
        Danger = Hex(0xFF9C8C),
        DangerSubtle = Hex(0x3C1B16),
        Info = Hex(0x9DB8F5),
        InfoSubtle = Hex(0x1A2440),
    };

    /// <summary>
    /// High contrast theme built from the active Windows system colors, so the user's contrast theme is
    /// respected instead of being replaced by the application's colors. Selected and hovered content is
    /// written in HighlightText on Highlight.
    /// </summary>
    public static ThemePalette HighContrast() => new()
    {
        Name = "HighContrast",
        IsHighContrast = true,
        IsDark = SystemColors.Window.GetBrightness() < 0.5f,
        Page = SystemColors.Window,
        Surface = SystemColors.Window,
        SurfaceAlt = SystemColors.Window,
        SurfaceHover = SystemColors.Highlight,
        Border = SystemColors.WindowText,
        BorderStrong = SystemColors.WindowText,
        Text = SystemColors.WindowText,
        TextSecondary = SystemColors.WindowText,
        TextMuted = SystemColors.WindowText,
        TextDisabled = SystemColors.GrayText,
        Accent = SystemColors.Highlight,
        AccentHover = SystemColors.Highlight,
        AccentPressed = SystemColors.Highlight,
        OnAccent = SystemColors.HighlightText,
        OnAccentMuted = SystemColors.HighlightText,
        AccentSubtle = SystemColors.Window,
        AccentText = SystemColors.HotTrack,
        Warm = SystemColors.WindowText,
        WarmSubtle = SystemColors.Window,
        WarmText = SystemColors.WindowText,
        Success = SystemColors.WindowText,
        SuccessSubtle = SystemColors.Window,
        Warning = SystemColors.WindowText,
        WarningSubtle = SystemColors.Window,
        Danger = SystemColors.WindowText,
        DangerSubtle = SystemColors.Window,
        Info = SystemColors.WindowText,
        InfoSubtle = SystemColors.Window,
    };

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}
