using System.ComponentModel;

namespace EtkinlikYonetim.UI.Theming;

/// <summary>How a status pill or a second line of a list row is colored.</summary>
internal enum RowTone
{
    Neutral,

    /// <summary>A seat that holds a ticket, or a ticket used at the entrance, in the accent color.</summary>
    Accent,

    /// <summary>A warm mark, such as a place on the waiting list.</summary>
    Warm,

    /// <summary>Something that needs attention, such as a cancelled event or a disabled account.</summary>
    Warning,
}

/// <summary>
/// What a list row shows: a primary line, an optional secondary line and an optional status pill at the
/// right of the first line. The pill is measured first and keeps its whole text; only the primary line
/// gives way (with an ellipsis), so a long event or person name never hides the seats or the status.
/// </summary>
internal readonly record struct ListRow(string Primary, string? Secondary = null, RowTone Tone = RowTone.Neutral, string? Status = null, RowTone StatusTone = RowTone.Neutral);

/// <summary>
/// A list painted with the theme: one or two lines per row, an optional status pill, hairline dividers, an
/// accent edge on the selected row and a message in place of the rows while the list loads or when it is
/// empty. The items, selection, keyboard and accessibility stay those of the native list box (an item's
/// text is its <see cref="object.ToString"/>). Under high contrast selected rows use HighlightText on Highlight.
/// </summary>
internal sealed class ThemedListBox : ListBox, IThemeAware
{
    private const int WmPaint = 0x000F;
    private const TextFormatFlags LineFlags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
    private const TextFormatFlags PillFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    private string _emptyText = string.Empty;
    private bool _loading;
    private bool _twoLines = true;

    public ThemedListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;

        // Whole rows only: the space left below the last row is the list's own background, so no row is cut.
        IntegralHeight = true;
        BorderStyle = BorderStyle.None;
        Font = Theme.Fonts.Body;
        Margin = Padding.Empty;
        if (!Theme.Current.IsHighContrast)
        {
            BackColor = Theme.Current.Surface;
            ForeColor = Theme.Current.Text;
        }

        ItemHeight = RowHeight();

        // A list that fills a card keeps room for a couple of rows; a smaller window scrolls instead.
        MinimumSize = new Size(0, 100);
    }

    /// <summary>The colors follow the palette (see <see cref="ThemeRefresher"/>); the rows are painted again.</summary>
    void IThemeAware.ApplyTheme(ThemeRemap remap) => Invalidate();

    /// <summary>Turns an item into the lines of its row; the item's text is used as a single line when not set.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<object, ListRow>? Describe { get; set; }

    /// <summary>Whether the rows have a second line; decides the row height.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool TwoLines
    {
        get => _twoLines;
        set
        {
            _twoLines = value;
            ItemHeight = RowHeight();
        }
    }

    /// <summary>
    /// Whether a single-line row wraps its whole text over as many lines as it needs instead of
    /// ending in an ellipsis. The rows are measured again when the list's width changes.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool WrapText
    {
        get => DrawMode == DrawMode.OwnerDrawVariable;
        set
        {
            _twoLines = !value && _twoLines;
            DrawMode = value ? DrawMode.OwnerDrawVariable : DrawMode.OwnerDrawFixed;
            IntegralHeight = !value;
            ItemHeight = RowHeight();
        }
    }

    /// <summary>Shown in the middle of the list while it has no rows.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText
    {
        get => _emptyText;
        set
        {
            _emptyText = value;
            Invalidate();
        }
    }

    /// <summary>Shown instead of <see cref="EmptyText"/> while <see cref="IsLoading"/> is set.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string LoadingText { get; set; } = "Yükleniyor…";

    /// <summary>A list request is on its way; an empty list says so instead of "nothing here".</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsLoading
    {
        get => _loading;
        set
        {
            if (_loading != value)
            {
                _loading = value;
                Invalidate();
            }
        }
    }

    /// <summary>
    /// Background, first line and second line colors of a row. Under high contrast a selected row is
    /// Highlight with HighlightText for both lines, never a palette color that could vanish on it.
    /// </summary>
    internal static (Color Back, Color Primary, Color Secondary) RowColors(ThemePalette palette, bool selected, RowTone tone)
    {
        ArgumentNullException.ThrowIfNull(palette);

        if (palette.IsHighContrast)
        {
            return selected ? (palette.Accent, palette.OnAccent, palette.OnAccent) : (palette.Surface, palette.Text, palette.Text);
        }

        return (selected ? palette.AccentSubtle : palette.Surface, palette.Text, ToneText(palette, tone));
    }

    /// <summary>
    /// Fill, border and text of a status pill. Under high contrast the pill is an outline in the row's text
    /// color, so on a selected (Highlight) row it is HighlightText as well.
    /// </summary>
    internal static (Color Fill, Color Border, Color Text) PillColors(ThemePalette palette, bool selected, RowTone tone)
    {
        ArgumentNullException.ThrowIfNull(palette);

        if (palette.IsHighContrast)
        {
            var text = selected ? palette.OnAccent : palette.Text;
            return (selected ? palette.Accent : palette.Surface, text, text);
        }

        return tone switch
        {
            RowTone.Accent => (palette.AccentSubtle, palette.Accent, palette.AccentText),
            RowTone.Warm => (palette.WarmSubtle, palette.Warm, palette.WarmText),
            RowTone.Warning => (palette.WarningSubtle, palette.Warning, palette.Warning),
            _ => (palette.SurfaceAlt, palette.Border, palette.TextSecondary),
        };
    }

    /// <summary>
    /// Splits the first line of a row: the status pill takes its whole width at the right, the primary
    /// text gets what is left (at least nothing). The pill never overlaps the text.
    /// </summary>
    internal static (Rectangle Primary, Rectangle Status) SplitLine(Rectangle line, int statusWidth, int gap)
    {
        if (statusWidth <= 0)
        {
            return (line, Rectangle.Empty);
        }

        var width = Math.Min(statusWidth, line.Width);
        var status = new Rectangle(line.Right - width, line.Y, width, line.Height);
        var primary = new Rectangle(line.X, line.Y, Math.Max(0, status.Left - gap - line.X), line.Height);
        return (primary, status);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ItemHeight = RowHeight();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ItemHeight = RowHeight();
    }

    private int _measuredWidth;

    protected override void OnMeasureItem(MeasureItemEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMeasureItem(e);
        if (!WrapText || e.Index < 0 || e.Index >= Items.Count)
        {
            return;
        }

        var text = (Describe?.Invoke(Items[e.Index]) ?? new ListRow(GetItemText(Items[e.Index]) ?? string.Empty)).Primary;
        var width = Math.Max(1, ClientSize.Width - Theme.Px(this, Spacing.M + 2) - Theme.Px(this, Spacing.M));
        var measured = TextRenderer.MeasureText(e.Graphics, text, Font, new Size(width, int.MaxValue), WrapFlags);
        e.ItemHeight = Math.Clamp(measured.Height + (2 * Theme.Px(this, Spacing.S)), RowHeight(), 255);
        _measuredWidth = ClientSize.Width;
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (WrapText && IsHandleCreated && Items.Count > 0 && ClientSize.Width != _measuredWidth && ClientSize.Width > 0)
        {
            // A variable height list measures its rows only when they are added.
            _measuredWidth = ClientSize.Width;
            BeginUpdate();
            try
            {
                RefreshItems();
            }
            finally
            {
                EndUpdate();
            }
        }
    }

    private const TextFormatFlags WrapFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            return;
        }

        var p = Theme.Current;
        var g = e.Graphics;
        var item = Items[e.Index];
        var row = Describe?.Invoke(item) ?? new ListRow(GetItemText(item) ?? string.Empty);
        var selected = (e.State & DrawItemState.Selected) != 0;
        var (back, primary, secondary) = RowColors(p, selected, row.Tone);
        using (var brush = new SolidBrush(back))
        {
            g.FillRectangle(brush, e.Bounds);
        }

        if (selected && !p.IsHighContrast)
        {
            using var edge = new SolidBrush(p.Accent);
            g.FillRectangle(edge, e.Bounds.X, e.Bounds.Y, Theme.Px(this, 3), e.Bounds.Height);
        }

        if (!p.IsHighContrast && e.Index < Items.Count - 1)
        {
            using var pen = new Pen(p.Border, Math.Max(1, Theme.Px(this, 1)));
            g.DrawLine(pen, e.Bounds.Left + Theme.Px(this, Spacing.M), e.Bounds.Bottom - 1, e.Bounds.Right - Theme.Px(this, Spacing.M), e.Bounds.Bottom - 1);
        }

        var left = e.Bounds.X + Theme.Px(this, Spacing.M + 2);
        var width = Math.Max(0, e.Bounds.Width - (left - e.Bounds.X) - Theme.Px(this, Spacing.M));
        var strong = Theme.Fonts.BodyStrong;
        var twoLines = row.Secondary is not null && TwoLines;
        var pad = Theme.Px(this, Spacing.S);
        var firstLine = twoLines
            ? new Rectangle(left, e.Bounds.Y + pad, width, strong.Height)
            : new Rectangle(left, e.Bounds.Y, width, e.Bounds.Height);
        var (primaryBounds, statusBounds) = SplitLine(firstLine, StatusWidth(g, row.Status), Theme.Px(this, Spacing.S));
        if (WrapText)
        {
            var area = new Rectangle(left, e.Bounds.Y + pad, width, Math.Max(0, e.Bounds.Height - (2 * pad)));
            TextRenderer.DrawText(g, row.Primary, Font, area, primary, back, WrapFlags | TextFormatFlags.EndEllipsis);
        }
        else
        {
            TextRenderer.DrawText(g, row.Primary, twoLines ? strong : Font, primaryBounds, primary, back, LineFlags);
        }
        if (row.Status is { Length: > 0 } status)
        {
            DrawPill(g, status, statusBounds, selected, row.StatusTone);
        }

        if (twoLines)
        {
            TextRenderer.DrawText(g, row.Secondary, Theme.Fonts.Caption, new Rectangle(left, firstLine.Bottom, width, Math.Max(0, e.Bounds.Bottom - firstLine.Bottom - pad)), secondary, back, LineFlags);
        }

        if ((e.State & DrawItemState.Focus) != 0 && (e.State & DrawItemState.NoFocusRect) == 0 && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(g, e.Bounds, primary, back);
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // The native list paints nothing without items; the empty or loading message is drawn on top.
        if (m.Msg == WmPaint && Items.Count == 0 && IsHandleCreated)
        {
            using var graphics = CreateGraphics();
            PaintTo(graphics);
        }
    }

    /// <summary>
    /// Paints the empty or loading message into <paramref name="graphics"/> while the list has no items.
    /// DrawToBitmap (used by the offscreen screenshot tool) only prints the native list, so it needs this entry point.
    /// </summary>
    internal void PaintTo(Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var message = _loading ? LoadingText : _emptyText;
        if (Items.Count > 0 || string.IsNullOrEmpty(message))
        {
            return;
        }

        var pad = Theme.Px(this, Spacing.L);
        var area = new Rectangle(pad, pad, Math.Max(0, ClientSize.Width - (2 * pad)), Math.Max(0, ClientSize.Height - (2 * pad)));
        TextRenderer.DrawText(
            graphics,
            message,
            Theme.Fonts.Body,
            area,
            Theme.Current.TextMuted,
            BackColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    private static Color ToneText(ThemePalette palette, RowTone tone) => tone switch
    {
        RowTone.Accent => palette.AccentText,
        RowTone.Warm => palette.WarmText,
        RowTone.Warning => palette.Warning,
        _ => palette.TextSecondary,
    };

    private int StatusWidth(Graphics graphics, string? status) =>
        string.IsNullOrEmpty(status)
            ? 0
            : TextRenderer.MeasureText(graphics, status, Theme.Fonts.Caption, Size.Empty, PillFlags).Width + (2 * Theme.Px(this, Spacing.S));

    private void DrawPill(Graphics graphics, string text, Rectangle bounds, bool selected, RowTone tone)
    {
        var (fill, border, color) = PillColors(Theme.Current, selected, tone);
        var height = Math.Min(bounds.Height, Theme.Fonts.Caption.Height + Theme.Px(this, 4));
        var pill = new Rectangle(bounds.X, bounds.Y + ((bounds.Height - height) / 2), bounds.Width, height);
        Painting.FillRounded(graphics, pill, height / 2F, fill, border, Math.Max(1F, Theme.PxF(this, 1)));
        TextRenderer.DrawText(graphics, text, Theme.Fonts.Caption, pill, color, fill, PillFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private int RowHeight()
    {
        var lines = TwoLines ? Theme.Fonts.BodyStrong.Height + Theme.Fonts.Caption.Height : Math.Max(Font.Height, Theme.Fonts.Caption.Height + 4);
        return Math.Min(255, lines + Theme.Px(this, TwoLines ? 2 * Spacing.S : Spacing.M + 2));
    }
}
