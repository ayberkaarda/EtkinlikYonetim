using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>
/// The cobalt panel on the left of the sign-in screen: the logo, the product name with the magenta brand
/// mark, one line on what the screen is for and a few short points. It holds no input and scrolls on its
/// own on a very small screen. Under high contrast it uses the window colors.
/// </summary>
internal sealed class BrandPanel : Panel
{
    public BrandPanel(string tagline, params (string Glyph, string Text)[] points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var p = Theme.Current;
        // The dark palette keeps the panel deep (a tinted ink) instead of a light accent block beside dark content.
        var deep = p.IsHighContrast || p.IsDark;
        var onBrand = deep ? p.Text : p.OnAccent;
        var onBrandMuted = p.IsHighContrast ? p.Text : p.IsDark ? p.TextSecondary : p.OnAccentMuted;
        Name = "brandPanel";
        AutoScroll = true;
        BackColor = p.IsHighContrast ? p.Surface : p.IsDark ? p.AccentSubtle : p.Accent;
        Padding = new Padding(Spacing.XL + Spacing.XS, Spacing.XXL, Spacing.XL, Spacing.XL);
        SetStyle(ControlStyles.ResizeRedraw, true);

        var logo = new GlyphBadge(string.Empty, Sizes.LogoBadge) { Name = "brandLogo", Image = AppAssets.Logo, Margin = new Padding(0, 0, 0, Spacing.L) };
        var name = new Label
        {
            Name = "brandName",
            Text = AppText.Title,
            AutoSize = true,
            Font = Theme.Fonts.Display,
            ForeColor = onBrand,
            UseMnemonic = false,
            Margin = Padding.Empty,
        };
        var rule = new BrandRule { Name = "brandRule", Margin = new Padding(2, Spacing.M, 0, Spacing.M) };
        var line = new Label
        {
            Name = "brandTagline",
            Text = tagline,
            AutoSize = true,
            Font = Theme.Fonts.Body,
            ForeColor = onBrandMuted,
            UseMnemonic = false,
            Margin = new Padding(0, 0, 0, Spacing.XL),
        };

        var list = new TableLayoutPanel
        {
            Name = "brandPoints",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < points.Length; i++)
        {
            var (glyph, text) = points[i];
            list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var badge = new GlyphBadge(glyph, 32, 12F)
            {
                Name = "brandPointBadge" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Fill = p.IsHighContrast ? null : p.IsDark ? p.Surface : p.AccentPressed,
                GlyphColor = p.IsHighContrast ? null : p.IsDark ? p.AccentText : p.OnAccent,
                Margin = new Padding(0, 0, Spacing.M, Spacing.M),
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
            };
            var label = new Label
            {
                Name = "brandPoint" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Text = text,
                AutoSize = true,
                Font = Theme.Fonts.Body,
                ForeColor = onBrand,
                UseMnemonic = false,
                Margin = new Padding(0, Spacing.XS + 2, 0, Spacing.M),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            };
            list.Controls.Add(badge, 0, i);
            list.Controls.Add(label, 1, i);
        }

        _stack = FormLayout.Stack("brandStack", logo, name, rule, line, list);
        logo.Dock = DockStyle.None;
        rule.Dock = DockStyle.None;
        logo.Anchor = rule.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        Controls.Add(_stack);
    }

    // Null while the constructor still sets up the panel (its own layout runs before the stack exists).
    private readonly TableLayoutPanel? _stack;

    /// <summary>
    /// A docked stack takes no part in automatic scrolling, so the scroll range is set to the height the stack
    /// needs at the panel's width: on a short window the points scroll instead of being cut off.
    /// </summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_stack is null)
        {
            return;
        }

        // The stack has just been laid out at the panel's width, so its height is what it needs. A scroll bar that
        // appears takes width and makes the text wrap onto more lines, so the stack is laid out again until the
        // range holds still.
        for (var pass = 0; pass < 3; pass++)
        {
            var needed = _stack.Height + Padding.Vertical;
            var minimum = needed > ClientSize.Height ? new Size(0, needed) : Size.Empty;
            if (AutoScrollMinSize == minimum)
            {
                return;
            }

            AutoScrollMinSize = minimum;
            base.OnLayout(levent);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        if (Theme.Current.IsHighContrast)
        {
            // A line separates the panel from the form when both use the window color.
            using var pen = new Pen(Theme.Current.Border, Math.Max(1, Theme.Px(this, 1)));
            e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        }
    }
}
