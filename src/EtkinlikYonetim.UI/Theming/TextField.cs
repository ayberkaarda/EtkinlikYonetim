namespace EtkinlikYonetim.UI.Theming;

/// <summary>
/// Frame around a borderless <see cref="TextBox"/>: the shared control height (or a taller box for a
/// multi-line text box), a rounded border and an accent focus ring, so text inputs line up with drop-down
/// lists and buttons. The text box itself stays a normal, focusable control (tab order, access keys and
/// screen readers are unchanged). Under high contrast the native border is kept.
/// </summary>
internal sealed class TextField : Control, IThemeAware
{
    private bool _hover;

    public TextField(TextBox input, int height = Sizes.ControlHeight)
    {
        ArgumentNullException.ThrowIfNull(input);

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        // The frame never takes the focus itself: a caption's access key and Tab move into the text box.
        SetStyle(ControlStyles.Selectable, false);
        Input = input;
        Name = input.Name + "Field";
        TabStop = false;
        Cursor = Cursors.IBeam;
        Margin = new Padding(0, Spacing.XS, 0, Spacing.XS);
        Font = Theme.Fonts.Body;
        Size = new Size(Math.Max(input.Width, 120), height);
        MinimumSize = new Size(0, height);

        // A text box that was given its own font (a code field) keeps it; any other takes the body font.
        if (input.Font == Control.DefaultFont)
        {
            input.Font = Theme.Fonts.Body;
        }

        input.Margin = Padding.Empty;
        input.BorderStyle = Theme.Current.IsHighContrast ? BorderStyle.FixedSingle : BorderStyle.None;
        input.GotFocus += (_, _) => Invalidate();
        input.LostFocus += (_, _) => Invalidate();
        input.MouseEnter += (_, _) => SetHover(true);
        input.MouseLeave += (_, _) => SetHover(false);
        input.EnabledChanged += (_, _) =>
        {
            SyncInputBackground();
            Invalidate();
        };
        input.ReadOnlyChanged += (_, _) =>
        {
            SyncInputBackground();
            Invalidate();
        };
        Controls.Add(input);
        SyncInputBackground();
    }

    public TextBox Input { get; }

    /// <summary>The native border returns under high contrast; the frame paints it otherwise.</summary>
    void IThemeAware.ApplyTheme(ThemeRemap remap)
    {
        Input.BorderStyle = Theme.Current.IsHighContrast ? BorderStyle.FixedSingle : BorderStyle.None;
        SyncInputBackground();
        PerformLayout();
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Input.Focus();
    }

    protected override void OnClick(EventArgs e)
    {
        Input.Focus();
        base.OnClick(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        SetHover(true);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        SetHover(false);
        base.OnMouseLeave(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var inset = Theme.Px(this, Spacing.S + 2);
        if (Input.Multiline)
        {
            var top = Theme.Px(this, Spacing.S - 2);
            Input.SetBounds(inset, top, Math.Max(0, Width - (2 * inset)), Math.Max(0, Height - (2 * top)));
            return;
        }

        var height = Input.PreferredHeight;
        if (height > Height && MinimumSize.Height < height)
        {
            // Large text needs a taller box than the shared control height; the frame grows with it.
            MinimumSize = new Size(MinimumSize.Width, height);
            return;
        }

        Input.SetBounds(inset, Math.Max(0, (Height - height) / 2), Math.Max(0, Width - (2 * inset)), height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Painting.ParentBackground(this));
        if (p.IsHighContrast)
        {
            return;
        }

        var enabled = Input.Enabled;
        var focused = Input.Focused;
        var border = !enabled ? p.Border : focused ? p.Accent : _hover ? p.TextSecondary : p.BorderStrong;
        var width = focused ? Theme.PxF(this, Sizes.FocusRing) : Math.Max(1F, Theme.PxF(this, 1));
        Painting.FillRounded(g, new RectangleF(0, 0, Width, Height), Theme.PxF(this, Radius.Control), Fill(p), border, width);
    }

    /// <summary>The borderless text box takes the field's fill, so the frame reads as one input.</summary>
    internal void SyncInputBackground()
    {
        var p = Theme.Current;
        if (!p.IsHighContrast)
        {
            Input.BackColor = Fill(p);
            Input.ForeColor = p.Text;
        }
    }

    /// <summary>Read-only and disabled inputs sit on the quiet surface, so they do not look editable.</summary>
    private Color Fill(ThemePalette p) => Input.Enabled && !Input.ReadOnly ? p.Surface : p.SurfaceAlt;

    private void SetHover(bool hover)
    {
        if (_hover != hover)
        {
            _hover = hover;
            Invalidate();
        }
    }
}

/// <summary>
/// The same frame as <see cref="TextField"/> around a borderless <see cref="NumericUpDown"/>, so a number
/// input lines up with the text fields. The up and down arrows, the keyboard and screen readers stay native.
/// </summary>
internal sealed class NumberField : Control, IThemeAware
{
    public NumberField(NumericUpDown input)
    {
        ArgumentNullException.ThrowIfNull(input);

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Input = input;
        Name = input.Name + "Field";
        TabStop = false;
        Margin = new Padding(0, Spacing.XS, 0, Spacing.XS);
        Font = Theme.Fonts.Body;
        Size = new Size(140, Sizes.ControlHeight);
        MinimumSize = new Size(0, Sizes.ControlHeight);

        input.Font = Theme.Fonts.Body;
        input.Margin = Padding.Empty;
        input.BorderStyle = Theme.Current.IsHighContrast ? BorderStyle.FixedSingle : BorderStyle.None;
        input.GotFocus += (_, _) => Invalidate();
        input.LostFocus += (_, _) => Invalidate();
        input.EnabledChanged += (_, _) =>
        {
            SyncInputBackground();
            Invalidate();
        };
        Controls.Add(input);
        SyncInputBackground();
    }

    public NumericUpDown Input { get; }

    void IThemeAware.ApplyTheme(ThemeRemap remap)
    {
        Input.BorderStyle = Theme.Current.IsHighContrast ? BorderStyle.FixedSingle : BorderStyle.None;
        SyncInputBackground();
        PerformLayout();
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Input.Focus();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var inset = Theme.Px(this, Spacing.S);
        var height = Input.PreferredHeight;
        if (height > Height && MinimumSize.Height < height)
        {
            MinimumSize = new Size(MinimumSize.Width, height);
            return;
        }

        Input.SetBounds(inset, Math.Max(0, (Height - height) / 2), Math.Max(0, Width - inset - Theme.Px(this, 3)), height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Painting.ParentBackground(this));
        if (p.IsHighContrast)
        {
            return;
        }

        var focused = Input.ContainsFocus;
        var border = !Input.Enabled ? p.Border : focused ? p.Accent : p.BorderStrong;
        var width = focused ? Theme.PxF(this, Sizes.FocusRing) : Math.Max(1F, Theme.PxF(this, 1));
        Painting.FillRounded(g, new RectangleF(0, 0, Width, Height), Theme.PxF(this, Radius.Control), Input.Enabled ? p.Surface : p.SurfaceAlt, border, width);
    }

    internal void SyncInputBackground()
    {
        var p = Theme.Current;
        if (!p.IsHighContrast)
        {
            Input.BackColor = Input.Enabled ? p.Surface : p.SurfaceAlt;
            Input.ForeColor = Input.Enabled ? p.Text : p.TextDisabled;
        }
    }
}

/// <summary>
/// A native date picker that does not show its own border, except under high contrast: the themed picker
/// draws the border inside its window, so a window region leaves those pixels out.
/// </summary>
internal sealed class DatePicker : DateTimePicker
{
    /// <summary>Width of the native border in device pixels.</summary>
    internal static int BorderWidth => SystemInformation.Border3DSize.Width;

    /// <summary>Shows or hides the native border, following the contrast setting.</summary>
    internal void ApplyBorder()
    {
        var previous = Region;
        Region = Theme.Current.IsHighContrast || Width <= 2 * BorderWidth || Height <= 2 * BorderWidth
            ? null
            : new Region(new Rectangle(BorderWidth, BorderWidth, Width - (2 * BorderWidth), Height - (2 * BorderWidth)));
        previous?.Dispose();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyBorder();
    }
}

/// <summary>
/// The same frame as <see cref="TextField"/> around a borderless <see cref="DatePicker"/>, so a date input
/// lines up with the text fields. The calendar, the keyboard and screen readers stay native. Under high
/// contrast the picker keeps its native border.
/// </summary>
internal sealed class DateField : Control, IThemeAware
{
    public DateField(DatePicker input)
    {
        ArgumentNullException.ThrowIfNull(input);

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Input = input;
        Name = input.Name + "Field";
        TabStop = false;
        Margin = new Padding(0, Spacing.XS, 0, Spacing.XS);
        Font = Theme.Fonts.Body;
        Size = new Size(150, Sizes.ControlHeight);
        MinimumSize = new Size(0, Sizes.ControlHeight);

        input.Font = Theme.Fonts.Body;
        input.Margin = Padding.Empty;
        input.GotFocus += (_, _) => Invalidate();
        input.LostFocus += (_, _) => Invalidate();
        input.EnabledChanged += (_, _) => Invalidate();
        Controls.Add(input);
    }

    public DatePicker Input { get; }

    void IThemeAware.ApplyTheme(ThemeRemap remap)
    {
        Input.ApplyBorder();
        PerformLayout();
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Input.Focus();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var height = Input.PreferredHeight;
        if (height > Height && MinimumSize.Height < height)
        {
            MinimumSize = new Size(MinimumSize.Width, height);
            return;
        }

        if (Theme.Current.IsHighContrast)
        {
            Input.SetBounds(0, Math.Max(0, (Height - height) / 2), Width, height);
            return;
        }

        // The hidden border lies inside the frame's inset, so the picker's text lines up with a text field's.
        var inset = Math.Max(0, Theme.Px(this, Spacing.S + 2) - DatePicker.BorderWidth - Theme.Px(this, 2));
        Input.SetBounds(inset, Math.Max(0, (Height - height) / 2), Math.Max(0, Width - (2 * inset)), height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Painting.ParentBackground(this));
        if (p.IsHighContrast)
        {
            return;
        }

        var focused = Input.ContainsFocus;
        var border = !Input.Enabled ? p.Border : focused ? p.Accent : p.BorderStrong;
        var width = focused ? Theme.PxF(this, Sizes.FocusRing) : Math.Max(1F, Theme.PxF(this, 1));
        Painting.FillRounded(g, new RectangleF(0, 0, Width, Height), Theme.PxF(this, Radius.Control), Input.Enabled ? p.Surface : p.SurfaceAlt, border, width);
    }
}
