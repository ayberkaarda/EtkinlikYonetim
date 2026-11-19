using System.ComponentModel;

namespace EtkinlikYonetim.UI.Theming;

/// <summary>A rounded surface with a hairline border that groups related content.</summary>
internal class CardPanel : Panel, IThemeAware
{
    private Color? _fill;

    public CardPanel()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Theme.Current.Surface;
        Padding = new Padding(Spacing.L);
        Margin = new Padding(0, 0, 0, Spacing.L);
    }

    /// <summary>Fill of the card (children paint on it too); the surface color when not set or under high contrast.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            BackColor = Theme.Current.IsHighContrast ? Theme.Current.Surface : value ?? Theme.Current.Surface;
            Invalidate(invalidateChildren: true);
        }
    }

    /// <summary>A narrow colored edge on the left, for example the warm mark of the card that holds a code.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? Edge { get; set; }

    /// <summary>The fill follows the palette through the background color; the edge is a stored color.</summary>
    void IThemeAware.ApplyTheme(ThemeRemap remap)
    {
        Edge = remap.Map(Edge);
        _fill = remap.Map(_fill);
        Invalidate(invalidateChildren: true);
    }

    /// <summary>
    /// An auto-sized card measures its stacked children by their preferred height at the card's
    /// width, so it grows when their content wraps (a docked panel would use their current height).
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (!AutoSize)
        {
            return base.GetPreferredSize(proposedSize);
        }

        // Without a usable proposal the card takes its parent's width (it is docked to fill or to the
        // top); its own width can still be the default of a control that was never laid out.
        var width = proposedSize.Width is > 1 and < int.MaxValue / 2
            ? proposedSize.Width
            : Parent is { } parent ? Math.Max(1, parent.DisplayRectangle.Width - Margin.Horizontal) : Width;
        var inner = Math.Max(0, width - Padding.Horizontal);
        var height = Padding.Vertical;
        foreach (Control child in Controls)
        {
            // Visible is not consulted: it reads false for every child until the window is shown.
            height += child.GetPreferredSize(new Size(inner, 0)).Height + child.Margin.Vertical;
        }

        return new Size(width, height);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Painting.ParentBackground(this));
        var fill = p.IsHighContrast ? p.Surface : BackColor;
        var bounds = new RectangleF(0, 0, Width, Height);
        Painting.FillRounded(g, bounds, Theme.PxF(this, Radius.Card), fill, p.Border, Math.Max(1F, Theme.PxF(this, 1)));
        if (Edge is { } edge && !p.IsHighContrast)
        {
            var width = Theme.PxF(this, 4);
            using var clip = Painting.RoundedRect(bounds, Theme.PxF(this, Radius.Card));
            g.SetClip(clip);
            using var brush = new SolidBrush(edge);
            g.FillRectangle(brush, 0, 0, width, Height);
            g.ResetClip();
        }
    }
}

/// <summary>
/// A card shaped like a printed ticket: a semicircle notch at the top and at the bottom edge where
/// <see cref="Perforation"/> separates the stub. Under high contrast it stays a plain card.
/// </summary>
internal sealed class TicketPanel : CardPanel
{
    /// <summary>The dashed line between the ticket and its stub; the notches are drawn in line with it.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? Perforation { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaintBackground(e);
        if (Perforation is null || Theme.Current.IsHighContrast)
        {
            return;
        }

        var x = Perforation.Width / 2F;
        for (Control? current = Perforation; current is not null && current != this; current = current.Parent)
        {
            x += current.Left;
        }

        var g = e.Graphics;
        var radius = Theme.PxF(this, 10);
        var previous = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var hole = new SolidBrush(Painting.ParentBackground(this));
        using var edge = new Pen(Theme.Current.Border, Math.Max(1F, Theme.PxF(this, 1)));
        foreach (var y in new[] { 0F, Height })
        {
            var circle = new RectangleF(x - radius, y - radius, 2 * radius, 2 * radius);
            g.FillEllipse(hole, circle);
            g.DrawEllipse(edge, circle);
        }

        g.SmoothingMode = previous;
    }
}

/// <summary>A dashed vertical line, the perforation between a ticket and its stub.</summary>
internal sealed class PerforationLine : Control
{
    public PerforationLine()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Size = new Size(2 * Spacing.XL, 1);
        Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.Separator;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var p = Theme.Current;
        e.Graphics.Clear(Painting.ParentBackground(this));
        using var pen = new Pen(p.IsHighContrast ? p.Text : p.BorderStrong, Math.Max(1F, Theme.PxF(this, 1.5F)))
        {
            DashPattern = [3F, 3F],
        };
        var x = Width / 2F;
        e.Graphics.DrawLine(pen, x, 0, x, Height);
    }
}

/// <summary>A square badge with a glyph (or the application logo) on a tinted background.</summary>
internal sealed class GlyphBadge : Control, IThemeAware
{
    public GlyphBadge(string glyph, int size, float glyphPoints = 14F)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Glyph = glyph;
        GlyphPoints = glyphPoints;
        Size = new Size(size, size);
        Margin = new Padding(0, 0, Spacing.M, 0);
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float GlyphPoints { get; set; }

    /// <summary>Glyph color; the accent text color when not set.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? GlyphColor { get; set; }

    /// <summary>Badge fill; the subtle accent when not set.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? Fill { get; set; }

    /// <summary>When set, the image is drawn instead of the glyph.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Image { get; set; }

    void IThemeAware.ApplyTheme(ThemeRemap remap)
    {
        Fill = remap.Map(Fill);
        GlyphColor = remap.Map(GlyphColor);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Painting.ParentBackground(this));
        if (Image is { } image)
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(image, ClientRectangle);
            return;
        }

        var fill = p.IsHighContrast ? p.Surface : Fill ?? p.AccentSubtle;
        Painting.FillRounded(g, new RectangleF(0, 0, Width, Height), Theme.PxF(this, Radius.Card), fill, p.IsHighContrast ? p.Border : null);
        Painting.DrawGlyph(g, Glyph, Theme.Fonts.Glyph(GlyphPoints), ClientRectangle, p.IsHighContrast ? p.Text : GlyphColor ?? p.AccentText);
    }
}

/// <summary>
/// A title docked along the top of a card that wraps onto more lines when the card is narrow (large text on
/// a small screen) instead of running past the card's edge; its height follows its width.
/// </summary>
internal sealed class HeadingLabel : Label
{
    private const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

    public HeadingLabel()
    {
        AutoSize = false;
        UseMnemonic = false;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 1 and < int.MaxValue / 2 ? proposedSize.Width : Width;
        return new Size(width, HeightFor(width));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Fit();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Fit();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Fit();
    }

    private int HeightFor(int width) =>
        TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, width - Padding.Horizontal), int.MaxValue), Flags).Height + Padding.Vertical;

    private void Fit()
    {
        var height = HeightFor(Width);
        if (Width > 0 && Height != height)
        {
            Height = height;
        }
    }
}

/// <summary>A short horizontal mark in the warm brand color, under a title.</summary>
internal sealed class BrandRule : Control
{
    public BrandRule()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Size = new Size(40, 3);
        AccessibleRole = AccessibleRole.Separator;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var p = Theme.Current;
        e.Graphics.Clear(Painting.ParentBackground(this));
        Painting.FillRounded(e.Graphics, new RectangleF(0, 0, Width, Height), Height / 2F, p.IsHighContrast ? p.Text : p.Warm);
    }
}

/// <summary>Severity of an inline message.</summary>
internal enum StatusKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// An inline message strip (the "info bar" pattern). Results and validation messages appear inside the
/// window instead of in a modal box, so nothing steals the focus; screen readers are told about the
/// change through the accessibility name.
/// </summary>
internal sealed class InfoBar : Control
{
    private const TextFormatFlags TextFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

    private StatusKind _kind;
    private bool _shown;

    public InfoBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Name = "statusBar";
        Dock = DockStyle.Top;
        Visible = false;
        TabStop = false;
        Font = Theme.Fonts.Body;
        Size = Size.Empty;
        AccessibleRole = AccessibleRole.Alert;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public StatusKind Kind => _kind;

    /// <summary>Whether a message is currently shown.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsShown => _shown;

    /// <summary>Space kept below the strip, inside its own height, so content does not touch it.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Gap { get; set; } = Spacing.M;

    public void Show(StatusKind kind, string message)
    {
        _kind = kind;
        Text = message;
        AccessibleName = message;
        _shown = true;
        Visible = true;
        UpdateHeight();
        Invalidate();
        if (IsHandleCreated)
        {
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }
    }

    public void Clear()
    {
        _shown = false;
        Visible = false;
        Text = string.Empty;
        AccessibleName = null;

        // A hidden strip keeps no height, so it never overlaps the content in layout checks.
        Height = 0;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateHeight();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Painting.ParentBackground(this));
        var (fill, accent, glyph) = Style(p);
        var box = new RectangleF(0, 0, Width, Height - Theme.Px(this, Gap));
        Painting.FillRounded(g, box, Theme.PxF(this, Radius.Card), fill, p.IsHighContrast ? p.Border : Blend(accent, fill), Math.Max(1F, Theme.PxF(this, 1)));

        // A narrow severity edge on the left, so the kind reads at a glance without relying on the glyph.
        if (!p.IsHighContrast)
        {
            using var clip = Painting.RoundedRect(box, Theme.PxF(this, Radius.Card));
            g.SetClip(clip);
            using var edge = new SolidBrush(accent);
            g.FillRectangle(edge, 0, 0, Theme.PxF(this, 4), box.Height);
            g.ResetClip();
        }

        var pad = Theme.Px(this, Spacing.M);
        var glyphSize = Theme.Px(this, 20);
        Painting.DrawGlyph(g, glyph, Theme.Fonts.Glyph(11F), new Rectangle(pad + Theme.Px(this, 2), pad, glyphSize, Math.Max(glyphSize, Font.Height)), accent);
        var textLeft = TextLeft();
        TextRenderer.DrawText(g, Text, Font, new Rectangle(textLeft, pad, Math.Max(0, Width - textLeft - pad), (int)box.Height - (2 * pad)), p.Text, TextFlags);
    }

    private int TextLeft() => Theme.Px(this, Spacing.M + 2) + Theme.Px(this, 20) + Theme.Px(this, Spacing.S);

    private void UpdateHeight()
    {
        if (!_shown || Width <= 0)
        {
            return;
        }

        var pad = Theme.Px(this, Spacing.M);
        var textLeft = TextLeft();
        var measured = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, Width - textLeft - pad), int.MaxValue), TextFlags);
        var height = Math.Max(measured.Height, Font.Height) + (2 * pad) + Theme.Px(this, Gap);
        if (Height != height)
        {
            Height = height;
        }
    }

    private (Color Fill, Color Accent, string Glyph) Style(ThemePalette p) => _kind switch
    {
        StatusKind.Success => (p.SuccessSubtle, p.Success, Glyphs.Success),
        StatusKind.Warning => (p.WarningSubtle, p.Warning, Glyphs.Warning),
        StatusKind.Error => (p.DangerSubtle, p.Danger, Glyphs.Error),
        _ => (p.InfoSubtle, p.Info, Glyphs.Info),
    };

    private static Color Blend(Color color, Color background) => Theme.Flatten(Color.FromArgb(70, color), background);
}
