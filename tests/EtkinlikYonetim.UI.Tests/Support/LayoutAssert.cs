using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI.Tests.Support;

/// <summary>
/// Layout checks on a form that is created but not shown: handles are created so the layout engine runs,
/// then every control must lie inside the area its parent really provides and must not overlap a sibling.
/// A scrolling parent provides its whole scrollable area (the body of a window that is smaller than its
/// content); everything outside it (header, side panel, footer) must lie inside the window itself. Controls
/// a screen hid on purpose (<see cref="FormLayout.SetShown"/>) and the hidden message strip are skipped.
/// </summary>
internal static class LayoutAssert
{
    /// <summary>Working areas of a small laptop, a common laptop and a large screen; sizes do not depend on the machine running the test.</summary>
    public static readonly Size[] Areas = [new(1024, 600), new(1366, 768), new(1920, 1040)];

    /// <summary>Text sizes that stand in for a larger font, a higher DPI or the Windows text scale.</summary>
    public static readonly float[] FontScales = [1F, 1.25F, 1.5F, 2F];

    /// <summary>
    /// Builds the screen for every combination of a stand-in working area and font scale (replacing the real
    /// screen and font metrics), requires it to open inside that area, and checks its layout.
    /// </summary>
    public static void FitsInsideOnEveryScreen(Func<Form> create)
    {
        ArgumentNullException.ThrowIfNull(create);

        var previous = Theme.Current;
        var previousScale = Theme.RenderScale;
        var previousArea = AppDialog.WorkingAreaOf;
        try
        {
            foreach (var size in Areas)
            {
                foreach (var scale in FontScales)
                {
                    Theme.Use(previous, scale);
                    var area = new Rectangle(Point.Empty, size);
                    AppDialog.WorkingAreaOf = _ => area;
                    using var form = create();
                    try
                    {
                        FitsInside(form);
                    }
                    catch (Xunit.Sdk.XunitException ex)
                    {
                        throw new Xunit.Sdk.XunitException($"[{area.Size}, font scale {scale}] {ex.Message}");
                    }

                    Assert.True(
                        form.Width <= area.Width && form.Height <= area.Height,
                        $"{form.GetType().Name} opens at {form.Size} in a {area.Size} working area at font scale {scale}.");
                }
            }
        }
        finally
        {
            AppDialog.WorkingAreaOf = previousArea;
            Theme.Use(previous, previousScale);
        }
    }

    public static void FitsInside(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        CreateHandles(form);
        form.PerformLayout();

        // Every control lies inside what its parent provides, so nothing is clipped at any level. A scrolling
        // parent provides its whole scrollable area, which the user reaches by scrolling.
        foreach (var control in Shown(form))
        {
            var parent = control.Parent!;
            var available = parent is ScrollableControl { AutoScroll: true }
                ? parent.DisplayRectangle
                : parent.ClientRectangle;
            available.Inflate(1, 1);
            Assert.True(
                control.Width == 0 || control.Height == 0 || available.Contains(control.Bounds),
                $"{Describe(control)} at {control.Bounds} lies outside {Describe(parent)} ({available}).");
        }

        // Fixed parts of the window (header, footer) lie inside the window; scrolling areas (the body, the brand
        // panel) are judged above, by what they scroll over.
        foreach (var control in Shown(form).Where(c => c.Width > 0 && c.Height > 0 && !InsideScroller(c)))
        {
            var bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            var window = form.ClientRectangle;
            window.Inflate(1, 1);
            Assert.True(window.Contains(bounds), $"{Describe(control)} at {bounds} lies outside the window {form.ClientSize}.");
        }

        foreach (var parent in Shown(form).Prepend(form))
        {
            var children = parent.Controls.Cast<Control>().Where(c => c.Width > 0 && c.Height > 0 && IsShown(c)).ToList();
            for (var i = 0; i < children.Count; i++)
            {
                for (var j = i + 1; j < children.Count; j++)
                {
                    Assert.False(
                        children[i].Bounds.IntersectsWith(children[j].Bounds),
                        $"{Describe(children[i])} {children[i].Bounds} overlaps {Describe(children[j])} {children[j].Bounds} in {Describe(parent)}.");
                }
            }
        }
    }

    /// <summary>The visible controls of <paramref name="root"/>; hidden ones and their children are left out.</summary>
    public static IEnumerable<Control> Shown(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (!IsShown(child))
            {
                continue;
            }

            yield return child;
            foreach (var nested in Shown(child))
            {
                yield return nested;
            }
        }
    }

    public static void CreateHandles(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        _ = control.Handle;
        foreach (Control child in control.Controls)
        {
            CreateHandles(child);
        }
    }

    private static bool IsShown(Control control) => !FormLayout.IsHidden(control) && control is not InfoBar { IsShown: false };

    private static bool InsideScroller(Control control)
    {
        for (var parent = control.Parent; parent is not null && parent is not Form; parent = parent.Parent)
        {
            if (parent is ScrollableControl { AutoScroll: true })
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(Control control) =>
        string.IsNullOrEmpty(control.Name) ? $"{control.GetType().Name} '{control.Text}'" : control.Name;
}
