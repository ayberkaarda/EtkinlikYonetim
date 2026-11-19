using System.ComponentModel;

namespace EtkinlikYonetim.UI.Theming;

/// <summary>Visual weight of a button: one primary action per area, the rest secondary.</summary>
internal enum ButtonKind
{
    Primary,
    Secondary,

    /// <summary>A destructive action, such as cancelling an event or a registration.</summary>
    Danger,

    /// <summary>A quiet button without a border, for navigation and small repeated actions.</summary>
    Subtle,
}

/// <summary>
/// A standard <see cref="Button"/> (keyboard, mnemonics, accessibility and AcceptButton behavior are
/// unchanged) painted with the theme: rounded corners, hover and pressed states, an optional leading
/// glyph and a visible focus ring. Under high contrast the native button is used.
/// </summary>
internal sealed class ThemedButton : Button, IThemeAware
{
    private const float GlyphPoints = 10F;

    /// <summary>Logical width of the glyph and the gap after it.</summary>
    private const int GlyphRoom = 22;

    private bool _hover;
    private bool _pressed;
    private string _glyph = string.Empty;

    public ThemedButton(ButtonKind kind = ButtonKind.Secondary)
    {
        Kind = kind;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(kind == ButtonKind.Subtle ? Sizes.ControlHeight : Sizes.ButtonMinWidth, Sizes.ControlHeight);
        Padding = new Padding(Spacing.M, 0, Spacing.M, 0);
        Margin = new Padding(Spacing.XS);
        Font = Theme.Fonts.BodyStrong;
        Cursor = Cursors.Hand;

        ApplyStyles();
    }

    /// <summary>Native drawing under high contrast, custom drawing otherwise.</summary>
    private void ApplyStyles()
    {
        const ControlStyles Painted = ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor;
        if (Theme.Current.IsHighContrast)
        {
            SetStyle(Painted, false);
            FlatStyle = FlatStyle.System;
        }
        else
        {
            SetStyle(Painted, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
        }

        Glyph = _glyph; // The glyph's room depends on whether it is drawn.
        Invalidate();
    }

    void IThemeAware.ApplyTheme(ThemeRemap remap) => ApplyStyles();

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get; set; }

    /// <summary>Icon font character drawn before the text; none when empty or under high contrast.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph
    {
        get => _glyph;
        set
        {
            _glyph = value ?? string.Empty;

            // The glyph takes room on the left, so the automatic size keeps the whole text.
            Padding = new Padding(DrawsGlyph ? Spacing.M + GlyphRoom : Spacing.M, 0, Spacing.M, 0);
        }
    }

    private bool DrawsGlyph => !Theme.Current.IsHighContrast && _glyph.Length > 0 && Theme.Fonts.Glyph(GlyphPoints) is not null;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        ArgumentNullException.ThrowIfNull(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        ArgumentNullException.ThrowIfNull(pevent);
        if (Theme.Current.IsHighContrast)
        {
            base.OnPaint(pevent);
            return;
        }

        var p = Theme.Current;
        var g = pevent.Graphics;
        g.Clear(Painting.ParentBackground(this));

        var (fill, border, text) = Colors(p);
        var radius = Theme.PxF(this, Radius.Control);
        var ring = Theme.PxF(this, Sizes.FocusRing);
        var bounds = new RectangleF(0, 0, Width, Height);
        Painting.FillRounded(g, bounds, radius, fill, border);

        if (Focused && ShowFocusCues)
        {
            // Primary: a ring in the text color inside the fill; others: an accent ring on the edge.
            var primary = Kind == ButtonKind.Primary;
            var focus = primary ? RectangleF.Inflate(bounds, -ring, -ring) : bounds;
            Painting.FillRounded(g, focus, radius, Color.Transparent, primary ? p.OnAccent : p.Accent, ring);
        }

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
            | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix);
        var alignLeft = TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft;
        if (!DrawsGlyph)
        {
            var area = alignLeft ? Rectangle.FromLTRB(Theme.Px(this, Spacing.M), 0, Width, Height) : ClientRectangle;
            TextRenderer.DrawText(g, Text, Font, area, text, flags | (alignLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
            return;
        }

        // Glyph and text are centered together as one group, or start at the left padding for a left aligned button.
        var glyphWidth = Theme.Px(this, 16);
        var gap = Theme.Px(this, 6);
        var textWidth = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.SingleLine | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix)).Width;
        var left = alignLeft
            ? Theme.Px(this, Spacing.M)
            : Math.Max(Theme.Px(this, Spacing.S), (Width - (glyphWidth + gap + textWidth)) / 2);
        var glyphColor = !Enabled ? p.TextDisabled : Kind switch
        {
            ButtonKind.Primary => text,
            ButtonKind.Danger => p.Danger,
            _ => p.AccentText,
        };
        Painting.DrawGlyph(g, Glyph, Theme.Fonts.Glyph(GlyphPoints), new Rectangle(left, 0, glyphWidth, Height), glyphColor);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(left + glyphWidth + gap, 0, Math.Max(0, Width - left - glyphWidth - gap), Height), text, flags);
    }

    private (Color Fill, Color? Border, Color Text) Colors(ThemePalette p)
    {
        if (!Enabled)
        {
            return Kind == ButtonKind.Subtle
                ? (Color.Transparent, null, p.TextDisabled)
                : (p.SurfaceAlt, p.Border, p.TextDisabled);
        }

        return Kind switch
        {
            ButtonKind.Primary => (_pressed ? p.AccentPressed : _hover ? p.AccentHover : p.Accent, null, p.OnAccent),
            ButtonKind.Danger => (_pressed || _hover ? p.DangerSubtle : p.Surface, p.Danger, p.Danger),
            ButtonKind.Subtle => (_pressed ? p.Border : _hover ? p.SurfaceHover : Color.Transparent, null, p.AccentText),
            _ => (_pressed ? p.Border : _hover ? p.SurfaceHover : p.Surface, p.BorderStrong, p.Text),
        };
    }
}
