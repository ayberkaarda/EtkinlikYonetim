using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.Screenshots;

/// <summary>
/// Draws a form that is never shown. Window handles are created without making anything visible,
/// the content is painted with <see cref="Control.DrawToBitmap"/> and a neutral window frame is
/// drawn around it, so the image does not depend on the desktop theme of the machine running the tool.
/// </summary>
internal static class OffscreenCapture
{
    /// <param name="form">The screen, built at the render scale of the image but never given a window.</param>
    /// <param name="clientSize">The client size the screen has at 1x (after it fitted itself to its content).</param>
    /// <param name="scale">1 or 2; the layout is scaled like a monitor of that DPI factor.</param>
    public static Bitmap Capture(Form form, Size clientSize, float scale)
    {
        ArgumentNullException.ThrowIfNull(form);

        // The form's content moves to a hidden, parentless panel: Windows Forms limits a form to the size
        // of the screen, which the 2x images exceed, and the panel has no frame to cut away. The panel
        // is never visible, so its window stays hidden under the framework's parking window.
        using var host = new Panel
        {
            Visible = false,
            BackColor = form.BackColor,
            ForeColor = form.ForeColor,
            Font = form.Font,
            Size = clientSize,
        };
        host.Controls.AddRange(form.Controls.Cast<Control>().ToArray());

        // Scaled before the windows exist: controls that size themselves from their fonts when their window
        // is created (drop-down lists, date boxes, lists) then measure at the final scale only once.
        if (Math.Abs(scale - 1F) > 0.001F)
        {
            host.Scale(new SizeF(scale, scale));
        }

        CreateHandles(host);

        host.Size = new Size((int)Math.Round(clientSize.Width * scale), (int)Math.Round(clientSize.Height * scale));
        // Auto-sized panels settle after their children: lay out top-down, bottom-up, then top-down again.
        LayoutAll(host);
        LayoutChildrenFirst(host);
        LayoutAll(host);

        // Text does not grow exactly with the scale (line heights round per font size), so a column that just fit
        // at 1x can need a few more pixels at 2x: the image grows until nothing is cut off.
        for (var pass = 0; pass < 10; pass++)
        {
            var missing = MissingHeight(host);
            if (missing <= 0)
            {
                break;
            }

            host.Height += missing;
            LayoutAll(host);
            LayoutChildrenFirst(host);
            LayoutAll(host);
        }

        Application.DoEvents();

        using var content = new Bitmap(host.Width, host.Height, PixelFormat.Format32bppArgb);
        host.DrawToBitmap(content, new Rectangle(Point.Empty, host.Size));
        PaintComboBoxes(host, content);
        return Frame(content, new Rectangle(Point.Empty, host.Size), form, scale);
    }

    public static void Save(Bitmap bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>Handles of hidden child controls are not created on their own; WM_PRINT skips controls without one.</summary>
    private static void CreateHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls)
        {
            CreateHandles(child);
        }
    }

    /// <summary>
    /// Drop-down lists and list boxes answer WM_PRINT natively: the themed drawing of the closed drop-down
    /// boxes and the message of empty lists are added on top.
    /// </summary>
    private static void PaintComboBoxes(Control root, Bitmap target)
    {
        using var graphics = Graphics.FromImage(target);
        foreach (var control in Descendants(root))
        {
            if (control.Width <= 0 || control.Height <= 0 || FormLayout.IsHidden(control))
            {
                continue;
            }

            // WM_PRINT ignores a window region: the border a date picker's region leaves out is covered with
            // the field's fill, as the screen shows it.
            if (control is DatePicker { Region: not null } date)
            {
                var origin = Point.Empty;
                for (Control? current = date; current is not null && current != root; current = current.Parent)
                {
                    origin.Offset(current.Left, current.Top);
                }

                var b = DatePicker.BorderWidth;
                using var fill = new SolidBrush(date.Enabled ? Theme.Current.Surface : Theme.Current.SurfaceAlt);
                graphics.FillRectangle(fill, origin.X, origin.Y, date.Width, b);
                graphics.FillRectangle(fill, origin.X, origin.Y + date.Height - b, date.Width, b);
                graphics.FillRectangle(fill, origin.X, origin.Y, b, date.Height);
                graphics.FillRectangle(fill, origin.X + date.Width - b, origin.Y, b, date.Height);
                continue;
            }

            // A multi-line edit box prints its frame but not its text; the text is drawn the way the box wraps it.
            if (control is TextBox { Multiline: true, TextLength: > 0 } box)
            {
                var at = Point.Empty;
                for (Control? current = box; current is not null && current != root; current = current.Parent)
                {
                    at.Offset(current.Left, current.Top);
                }

                var margin = box.Font.Height / 6;
                TextRenderer.DrawText(
                    graphics,
                    box.Text,
                    box.Font,
                    new Rectangle(at.X + margin, at.Y + 1, box.ClientSize.Width - (2 * margin), box.ClientSize.Height - 2),
                    box.ForeColor,
                    box.BackColor,
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
                continue;
            }

            Action<Graphics>? paint = control switch
            {
                ThemedComboBox combo => combo.PaintTo,
                ThemedListBox { Items.Count: 0 } list => list.PaintTo,
                _ => null,
            };
            if (paint is null)
            {
                continue;
            }

            var location = Point.Empty;
            for (Control? current = control; current is not null && current != root; current = current.Parent)
            {
                location.Offset(current.Left, current.Top);
            }

            using var image = new Bitmap(control.Width, control.Height, PixelFormat.Format24bppRgb);
            using (var controlGraphics = Graphics.FromImage(image))
            {
                controlGraphics.Clear(control.BackColor);
                paint(controlGraphics);
            }

            graphics.DrawImageUnscaled(image, location);
        }
    }

    /// <summary>How far a shown control reaches below the visible area of its container.</summary>
    private static int MissingHeight(Control root)
    {
        var missing = 0;
        foreach (var control in Descendants(root))
        {
            if (control.Width > 0 && control.Height > 0 && !FormLayout.IsHidden(control) && control is not InfoBar { IsShown: false })
            {
                // A container's padding is part of the design (a card's border and inner space), so running into it counts.
                var parent = control.Parent!;
                var bottom = parent is ScrollableControl { AutoScroll: true } ? parent.ClientSize.Height : parent.DisplayRectangle.Bottom;
                missing = Math.Max(missing, control.Bottom - bottom);
            }
        }

        return missing;
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static void LayoutChildrenFirst(Control control)
    {
        foreach (Control child in control.Controls)
        {
            LayoutChildrenFirst(child);
        }

        control.PerformLayout();
    }

    private static void LayoutAll(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls)
        {
            LayoutAll(child);
        }
    }

    /// <summary>A light title bar with icon, title and caption glyphs, a hairline border and rounded corners.</summary>
    private static Bitmap Frame(Bitmap window, Rectangle client, Form form, float scale)
    {
        var p = Theme.Current;
        int S(float value) => (int)Math.Round(value * scale);
        var titleHeight = S(32);
        var border = Math.Max(1, S(1));
        var width = client.Width + (2 * border);
        var height = client.Height + titleHeight + border;

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var shape = Painting.RoundedRect(new RectangleF(0, 0, width - 1, height - 1), S(8));
        graphics.SetClip(shape);
        // GDI text needs an opaque target, so the title bar is drawn on its own bitmap first.
        using (var bar = TitleBar(width, titleHeight + border, form, scale))
        {
            graphics.DrawImage(bar, 0, 0);
        }

        graphics.DrawImage(window, new Rectangle(border, titleHeight, client.Width, client.Height), client, GraphicsUnit.Pixel);
        graphics.ResetClip();
        using var pen = new Pen(Color.FromArgb(70, 0, 0, 0), border);
        graphics.DrawPath(pen, shape);
        return result;
    }

    private static Bitmap TitleBar(int width, int height, Form form, float scale)
    {
        var p = Theme.Current;
        int S(float value) => (int)Math.Round(value * scale);
        var bar = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bar);
        graphics.Clear(p.Surface);

        var iconSize = S(16);
        using (var icon = new Icon(AppAssets.Icon, new Size(iconSize, iconSize)))
        {
            graphics.DrawIcon(icon, new Rectangle(S(12), (height - iconSize) / 2, iconSize, iconSize));
        }

        using var titleFont = new Font("Segoe UI", 9F * scale, FontStyle.Regular, GraphicsUnit.Point);
        TextRenderer.DrawText(graphics, form.Text, titleFont, new Rectangle(S(38), 0, width - S(200), height), p.Text, p.Surface,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // The caption buttons the window has (minimize, maximize, close), right aligned.
        var resizable = form.FormBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;
        string[] glyphs = [.. new[] { (form.MinimizeBox, "\uE921"), (form.MaximizeBox && resizable, "\uE922"), (true, "\uE8BB") }.Where(g => g.Item1).Select(g => g.Item2)];
        using var glyphFont = Theme.GlyphFamily is { } family ? new Font(family, 7.5F * scale, FontStyle.Regular, GraphicsUnit.Point) : null;
        var buttonWidth = S(46);
        for (var i = 0; i < glyphs.Length; i++)
        {
            var x = width - (buttonWidth * (glyphs.Length - i));
            Painting.DrawGlyph(graphics, glyphs[i], glyphFont, new Rectangle(x, 0, buttonWidth, height), p.Text);
        }

        return bar;
    }
}
