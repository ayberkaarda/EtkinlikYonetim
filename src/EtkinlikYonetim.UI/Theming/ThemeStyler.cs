namespace EtkinlikYonetim.UI.Theming;

/// <summary>
/// Applies the current palette to standard Windows Forms controls, so forms do not set colors one by
/// one. Custom painted controls read the palette themselves. Under high contrast nothing is recolored
/// and the system colors stay in effect.
/// </summary>
internal static class ThemeStyler
{
    /// <summary>Styles <paramref name="root"/> and all of its children.</summary>
    public static void Apply(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var p = Theme.Current;
        if (p.IsHighContrast)
        {
            return;
        }

        if (root is Form form)
        {
            form.BackColor = p.Page;
            form.ForeColor = p.Text;
        }

        Walk(root, p);
    }

    private static void Walk(Control parent, ThemePalette p)
    {
        foreach (Control control in parent.Controls)
        {
            switch (control)
            {
                case TextBox box when box.Parent is TextField field:
                    field.SyncInputBackground();
                    break;
                case NumericUpDown number when number.Parent is NumberField field:
                    field.SyncInputBackground();
                    break;
                case TextBox box:
                    if (box.BorderStyle == BorderStyle.Fixed3D)
                    {
                        box.BorderStyle = BorderStyle.FixedSingle;
                    }

                    box.BackColor = p.Surface;
                    box.ForeColor = p.Text;
                    break;
                case CheckBox check:
                    check.ForeColor = p.Text;
                    break;
            }

            if (control.HasChildren && control is not NumericUpDown)
            {
                Walk(control, p);
            }
        }
    }
}
