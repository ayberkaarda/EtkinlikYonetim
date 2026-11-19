using System.Reflection;
using System.Runtime.CompilerServices;

namespace EtkinlikYonetim.UI.Theming;

/// <summary>A control that decides how it is drawn when it is created and has to redo that when the palette changes.</summary>
internal interface IThemeAware
{
    /// <summary>Switches between native and custom drawing for <see cref="Theme.Current"/>; colors kept in properties go through <paramref name="remap"/>.</summary>
    void ApplyTheme(ThemeRemap remap);
}

/// <summary>Translates a color of one palette into the same role of another.</summary>
internal sealed class ThemeRemap(ThemePalette from, ThemePalette to)
{
    private static readonly PropertyInfo[] Roles = [.. typeof(ThemePalette).GetProperties().Where(p => p.PropertyType == typeof(Color))];

    public ThemePalette From { get; } = from;

    public ThemePalette To { get; } = to;

    /// <summary>The role of <paramref name="color"/> in the old palette, when it is one of its colors.</summary>
    public string? RoleOf(Color color) =>
        Roles.FirstOrDefault(role => ((Color)role.GetValue(From)!).ToArgb() == color.ToArgb())?.Name;

    public Color In(string role) => (Color)typeof(ThemePalette).GetProperty(role)!.GetValue(To)!;

    /// <summary>The same role in the new palette; colors that are not palette colors stay as they are.</summary>
    public Color Map(Color color) => RoleOf(color) is { } role ? In(role) : color;

    /// <summary>For colors kept in a property: only a change between two painted palettes needs one; high contrast ignores them.</summary>
    public Color? Map(Color? color) =>
        color is { } value && !From.IsHighContrast && !To.IsHighContrast ? Map(value) : color;
}

/// <summary>Brings the controls of an open window to a new palette without rebuilding them.</summary>
internal static class ThemeRefresher
{
    private sealed class Roles
    {
        public string? Back;
        public string? Fore;
    }

    // What the colors of a control were before high contrast replaced them, to give the same roles back afterwards.
    private static readonly ConditionalWeakTable<Control, Roles> Remembered = [];

    public static void Refresh(Control root, ThemePalette old)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(old);

        var remap = new ThemeRemap(old, Theme.Current);
        root.SuspendLayout();
        try
        {
            Walk(root, remap);
        }
        finally
        {
            root.ResumeLayout(true);
        }

        ThemeStyler.Apply(root);
        root.Invalidate(true);
    }

    private static void Walk(Control control, ThemeRemap remap)
    {
        Recolor(control, remap);
        if (control is IThemeAware aware)
        {
            aware.ApplyTheme(remap);
        }

        foreach (var child in control.Controls.Cast<Control>().ToList())
        {
            Walk(child, remap);
        }
    }

    private static void Recolor(Control control, ThemeRemap remap)
    {
        var memory = Remembered.GetOrCreateValue(control);
        var back = Translate(control.BackColor, ref memory.Back, remap);
        var fore = Translate(control.ForeColor, ref memory.Fore, remap);
        if (back != control.BackColor)
        {
            control.BackColor = back;
        }

        if (fore != control.ForeColor)
        {
            control.ForeColor = fore;
        }
    }

    private static Color Translate(Color color, ref string? remembered, ThemeRemap remap)
    {
        if (remap.From.IsHighContrast && remembered is { } role)
        {
            // Many roles share one system color under high contrast; the remembered role says which one it was.
            return remap.In(role);
        }

        remembered = remap.RoleOf(color);
        return remembered is null ? color : remap.In(remembered);
    }
}
