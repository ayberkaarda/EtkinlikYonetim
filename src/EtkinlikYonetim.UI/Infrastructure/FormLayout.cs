using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>
/// Factories for the building blocks of the screens: header, cards, labelled fields, inputs, bars and
/// buttons. Everything uses the theme tokens and logical pixels, and lays out with table panels, so no
/// screen positions or colors controls by hand.
/// </summary>
internal static class FormLayout
{
    /// <summary>Header of a screen: glyph badge (or the logo), title, one line of description and an optional aside.</summary>
    public static TableLayoutPanel Header(string title, string subtitle, string glyph, Image? logo = null)
    {
        var p = Theme.Current;
        var header = new TableLayoutPanel
        {
            Name = "header",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(Spacing.XL, Spacing.L, Spacing.XL, Spacing.L),
            BackColor = p.Surface,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Paint += (_, e) => Painting.DrawDivider(header, e.Graphics, top: false);

        var badge = new GlyphBadge(glyph, Sizes.HeaderBadge, 15F) { Name = "headerBadge", Image = logo, Anchor = AnchorStyles.Left };
        var titleLabel = new Label
        {
            Name = "headerTitle",
            Text = title,
            AutoSize = true,
            Font = Theme.Fonts.Title,
            ForeColor = p.Text,
            UseMnemonic = false,
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
        };
        var subtitleLabel = new Label
        {
            Name = "headerSubtitle",
            Text = subtitle,
            AutoSize = true,
            Font = Theme.Fonts.Body,
            ForeColor = p.TextSecondary,
            UseMnemonic = false,
            Margin = new Padding(0, Spacing.XS / 2, 0, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
        };
        header.Controls.Add(badge, 0, 0);
        header.SetRowSpan(badge, 2);
        header.Controls.Add(titleLabel, 1, 0);
        header.Controls.Add(subtitleLabel, 1, 1);
        return header;
    }

    /// <summary>A card with an optional section title above <paramref name="content"/>.</summary>
    public static CardPanel Card(string name, string? title, Control content, bool fill = true)
    {
        ArgumentNullException.ThrowIfNull(content);

        var card = new CardPanel { Name = name, Dock = fill ? DockStyle.Fill : DockStyle.Top };
        content.Dock = fill ? DockStyle.Fill : DockStyle.Top;
        card.Controls.Add(content);
        if (title is not null)
        {
            var heading = new HeadingLabel
            {
                Name = name + "Title",
                Text = title,
                Font = Theme.Fonts.Subtitle,
                ForeColor = Theme.Current.Text,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 0, 0, Spacing.M),
                Margin = Padding.Empty,
            };
            card.Controls.Add(heading);
        }

        if (!fill)
        {
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        return card;
    }

    /// <summary>
    /// A tinted card with a glyph badge and an explanation that wraps inside it, for what a screen needs the
    /// user to know before filling it in (who issues a setup key, what happens on success).
    /// </summary>
    public static CardPanel Note(string name, string glyph, Label text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var p = Theme.Current;
        var badge = new GlyphBadge(glyph, 32, 12F)
        {
            Name = name + "Badge",
            Fill = p.Surface,
            GlyphColor = p.AccentText,
            Margin = new Padding(0, 0, Spacing.M, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
        };
        text.AutoSize = true;
        text.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        text.Margin = new Padding(0, Spacing.XS + 2, 0, 0);
        var layout = new TableLayoutPanel
        {
            Name = name + "Layout",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(badge, 0, 0);
        layout.Controls.Add(text, 1, 0);

        var card = new CardPanel
        {
            Name = name,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Spacing.L, Spacing.M, Spacing.L, Spacing.M),
            Margin = new Padding(0, 0, 0, Spacing.L),
        };
        card.Fill = p.AccentSubtle;
        card.Edge = p.Accent;
        card.Controls.Add(layout);
        return card;
    }

    public static Label SectionTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Fonts.Subtitle,
        ForeColor = Theme.Current.Text,
        UseMnemonic = false,
        Margin = new Padding(0, 0, 0, Spacing.S),
    };

    /// <summary>Label of an input; an ampersand marks the Alt access key that moves to the input.</summary>
    public static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Fonts.BodyStrong,
        ForeColor = Theme.Current.Text,
        Margin = new Padding(0, 0, 0, Spacing.XS / 2),
    };

    /// <summary>Secondary explanatory text.</summary>
    public static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Fonts.Caption,
        ForeColor = Theme.Current.TextMuted,
        UseMnemonic = false,
        Margin = new Padding(0, Spacing.XS, 0, 0),
    };

    /// <summary>Body text that wraps inside its column.</summary>
    public static Label Text(string name, string text = "") => new()
    {
        Name = name,
        Text = text,
        AutoSize = true,
        Font = Theme.Fonts.Body,
        ForeColor = Theme.Current.Text,
        UseMnemonic = false,
        Margin = Padding.Empty,
    };

    public static ThemedButton Button(string name, string text, ButtonKind kind = ButtonKind.Secondary, string glyph = "") =>
        new(kind) { Name = name, Text = text, Glyph = glyph };

    /// <summary>A text box framed at the shared control height.</summary>
    public static TextField Input(TextBox box) => new(box);

    public static ThemedComboBox Picker(string name, int width, string placeholder = "") =>
        new() { Name = name, Width = width, Placeholder = placeholder };

    /// <summary>A multi-line text box framed like the other inputs, <paramref name="height"/> logical pixels tall.</summary>
    public static TextField Input(TextBox box, int height) => new(box, height);

    /// <summary>A number box framed at the shared control height.</summary>
    public static NumberField Number(NumericUpDown box) => new(box);

    /// <summary>
    /// The read-only field of a ticket code: a large monospaced text in a framed box, selectable so it can be
    /// copied.
    /// </summary>
    public static TextField CodeInput(TextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);

        box.ReadOnly = true;
        box.Font = Theme.Fonts.Code;
        box.TextAlign = HorizontalAlignment.Center;
        return new TextField(box, Sizes.ControlHeight + Spacing.M);
    }

    /// <summary>
    /// A labelled input: caption above, input below and an optional hint. The caption's access key
    /// moves the focus to the input, and the input gets the caption as its accessible name.
    /// </summary>
    public static TableLayoutPanel Field(string caption, Control input, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var field = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, Spacing.L, Spacing.M),
        };
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        field.Controls.Add(Caption(caption));
        if (input.Dock == DockStyle.None)
        {
            input.Anchor = input is ThemedComboBox or TextField or TextBox or NumberField
                ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
                : AnchorStyles.Left | AnchorStyles.Top;
        }

        input.Margin = new Padding(0, Spacing.XS, 0, 0);
        var accessible = input switch
        {
            TextField framed => framed.Input,
            NumberField number => number.Input,
            _ => input,
        };
        accessible.AccessibleName ??= caption.Replace("&", string.Empty, StringComparison.Ordinal);
        field.Controls.Add(input);
        if (hint is not null)
        {
            var hintLabel = Hint(hint);
            hintLabel.Name = (accessible.Name ?? "field") + "Hint";
            field.Controls.Add(hintLabel);
        }

        return field;
    }

    /// <summary>
    /// A button placed like a field without a caption, so it lines up with the inputs of a row of
    /// labelled fields (the empty caption takes the same height as theirs).
    /// </summary>
    public static TableLayoutPanel FieldAction(Control button)
    {
        ArgumentNullException.ThrowIfNull(button);

        var field = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, Spacing.M),
        };
        var caption = Caption(" ");
        caption.UseMnemonic = false;
        caption.AccessibleRole = AccessibleRole.None;
        field.Controls.Add(caption);
        button.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        button.Margin = new Padding(0, Spacing.XS, 0, 0);
        field.Controls.Add(button);
        return field;
    }

    /// <summary>
    /// Fields in one row with explicit column widths: a percentage shares the free width, zero sizes
    /// the column to its field.
    /// </summary>
    public static TableLayoutPanel FieldRow(string name, params (Control Field, float Percent)[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var row = new TableLayoutPanel
        {
            Name = name,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columns.Length,
            RowCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
        };
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (var i = 0; i < columns.Length; i++)
        {
            var (field, percent) = columns[i];
            if (i == columns.Length - 1)
            {
                field.Margin = new Padding(field.Margin.Left, field.Margin.Top, 0, field.Margin.Bottom); // The card's padding closes the row.
            }

            row.ColumnStyles.Add(percent > 0 ? new ColumnStyle(SizeType.Percent, percent) : new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(field, i, 0);
        }

        return row;
    }

    /// <summary>
    /// Controls stacked top to bottom in a single auto-sized column; unlike docked panels it grows
    /// when a child grows (for example when a label wraps on a narrow window or at a high DPI).
    /// </summary>
    public static TableLayoutPanel Stack(string name, params Control[] controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        var stack = new TableLayoutPanel
        {
            Name = name,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = controls.Length,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var control in controls)
        {
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (control is not ThemedButton)
            {
                control.Dock = DockStyle.Fill;
            }

            stack.Controls.Add(control);
        }

        return stack;
    }

    /// <summary>
    /// The page layout of a screen: rows top to bottom that take their content's height, except the
    /// rows marked to fill, which share the remaining height.
    /// </summary>
    public static TableLayoutPanel Page(string name, params (Control Control, bool Fill)[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var page = new TableLayoutPanel
        {
            Name = name,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows.Length,
            Margin = Padding.Empty,
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (control, fill) in rows)
        {
            page.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Fill;
            page.Controls.Add(control);
        }

        // Without a filling row the table would stretch the last row; an empty row takes the rest instead.
        if (!rows.Any(row => row.Fill))
        {
            page.RowCount++;
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }

        return page;
    }

    /// <summary>
    /// Two columns that share the width by <paramref name="leftPercent"/>; shares (not fixed widths) let both
    /// grow when larger text makes the window wider.
    /// </summary>
    public static TableLayoutPanel Columns(string name, Control left, float leftPercent, Control right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var layout = new TableLayoutPanel { Name = name, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, leftPercent));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 - leftPercent));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Dock = DockStyle.Fill;
        left.Margin = new Padding(0, 0, Spacing.S, 0);
        right.Dock = DockStyle.Fill;
        right.Margin = new Padding(Spacing.S, 0, 0, 0);
        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(right, 1, 0);
        return layout;
    }

    /// <summary>
    /// A single-row bar: the controls marked to fill share the free width, the others keep their size.
    /// Used for toolbars and action rows, so the controls line up on one center line.
    /// </summary>
    public static TableLayoutPanel Bar(string name, params (Control Control, bool Fill)[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var bar = new TableLayoutPanel
        {
            Name = name,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = items.Length,
            RowCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
        };
        bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (var i = 0; i < items.Length; i++)
        {
            var (control, fill) = items[i];
            bar.ColumnStyles.Add(fill ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));

            // Inputs and buttons share one height and line up at the top (a drop-down list only takes
            // its final height once its window exists); text is centered on the row.
            var vertical = control is Label ? AnchorStyles.None : AnchorStyles.Top;
            control.Anchor = (fill ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left) | vertical;
            bar.Controls.Add(control, i, 0);
        }

        return bar;
    }

    // Controls a screen hid on purpose. Visible reads false for every control until its window is shown, so
    // the screen's own choice is kept here for the size measurement and the layout checks.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Hidden = [];

    /// <summary>Shows or hides <paramref name="control"/>; a hidden control takes no room in its table and is not measured.</summary>
    public static void SetShown(Control control, bool shown)
    {
        ArgumentNullException.ThrowIfNull(control);

        control.Visible = shown;
        if (shown)
        {
            Hidden.Remove(control);
        }
        else
        {
            Hidden.AddOrUpdate(control, Hidden);
        }
    }

    /// <summary>Whether the screen hid <paramref name="control"/> (or one of its containers) with <see cref="SetShown"/>.</summary>
    public static bool IsHidden(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent)
        {
            if (Hidden.TryGetValue(current, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An empty column for <see cref="Bar"/> that pushes the following items to the right.</summary>
    public static Control Spacer(string name = "spacer") => new Panel { Name = name, Height = 1, Margin = Padding.Empty, TabStop = false };
}
