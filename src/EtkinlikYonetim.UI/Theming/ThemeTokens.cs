namespace EtkinlikYonetim.UI.Theming;

/// <summary>Spacing scale in logical pixels (96 DPI); layouts are scaled by Windows Forms for the monitor.</summary>
internal static class Spacing
{
    public const int XS = 4;
    public const int S = 8;
    public const int M = 12;
    public const int L = 16;
    public const int XL = 24;
    public const int XXL = 32;

    /// <summary>All steps in ascending order.</summary>
    public static IReadOnlyList<int> Scale { get; } = [XS, S, M, L, XL, XXL];
}

/// <summary>Corner radii in logical pixels; generous, like the corners of a printed ticket.</summary>
internal static class Radius
{
    /// <summary>Buttons, inputs and status pills.</summary>
    public const int Control = 8;

    /// <summary>Cards, banners, badges and the ticket.</summary>
    public const int Card = 12;
}

/// <summary>Fixed control sizes in logical pixels.</summary>
internal static class Sizes
{
    /// <summary>Height of buttons, text fields and drop-down lists, so a row of them lines up.</summary>
    public const int ControlHeight = 34;

    public const int ButtonMinWidth = 96;

    /// <summary>Glyph badge in screen headers and message windows.</summary>
    public const int HeaderBadge = 40;

    /// <summary>Logo badge of the brand panel.</summary>
    public const int LogoBadge = 56;

    /// <summary>Width of the brand panel on the sign-in screen.</summary>
    public const int BrandPanelWidth = 300;

    /// <summary>Height of a multi-line description field.</summary>
    public const int NoteHeight = 72;

    /// <summary>Side of the QR code on the ticket.</summary>
    public const int QrCode = 200;

    public const int FocusRing = 2;
}

/// <summary>One step of the type ramp; sizes are in points. <paramref name="Fallback"/> is used where <paramref name="Family"/> is not installed.</summary>
internal readonly record struct FontToken(string Family, float Size, FontStyle Style = FontStyle.Regular, string? Fallback = null);

/// <summary>
/// Type ramp. Headings use Bahnschrift SemiBold, the DIN-like face of Windows that reads like the signage of a
/// venue and the print of a ticket; where it is missing they fall back to Segoe UI Semibold. Body text is
/// Segoe UI at 9.75 pt, so lists of participants and registrations stay quick to scan. Hierarchy comes from
/// size and weight, never from color alone. Ticket codes use a monospaced face so that similar characters
/// stay apart when a code is read aloud or typed by hand at the entrance.
/// Display &gt; Title &gt; Subtitle &gt; Body &gt; Caption.
/// </summary>
internal static class Typography
{
    public const string Family = "Segoe UI";
    public const string SemiboldFamily = "Segoe UI Semibold";
    public const string HeadingFamily = "Bahnschrift SemiBold";
    public const string MonoFamily = "Consolas";

    public static readonly FontToken Display = new(HeadingFamily, 20F, Fallback: SemiboldFamily);
    public static readonly FontToken Title = new(HeadingFamily, 15F, Fallback: SemiboldFamily);
    public static readonly FontToken Subtitle = new(HeadingFamily, 12F, Fallback: SemiboldFamily);
    public static readonly FontToken Body = new(Family, 9.75F);
    public static readonly FontToken BodyStrong = new(SemiboldFamily, 9.75F);
    public static readonly FontToken Caption = new(Family, 9F);

    /// <summary>A ticket code (on the ticket and in the entrance field).</summary>
    public static readonly FontToken Code = new(MonoFamily, 14F, FontStyle.Bold);

    /// <summary>The answer at the entrance and the check-in counter, read from a step away.</summary>
    public static readonly FontToken Signal = new(HeadingFamily, 22F, Fallback: SemiboldFamily);

    /// <summary>Text sizes from the smallest to the largest; used to check the ramp is ordered.</summary>
    public static IReadOnlyList<FontToken> Ramp { get; } = [Caption, Body, Subtitle, Title, Display];
}

/// <summary>Code points of Segoe Fluent Icons / Segoe MDL2 Assets used by the application.</summary>
internal static class Glyphs
{
    public const string Calendar = "";
    public const string Person = "";
    public const string People = "";
    public const string AddPerson = "";
    public const string Lock = "";
    public const string Key = "";
    public const string Admin = "";
    public const string Add = "";
    public const string Save = "";
    public const string Delete = "";
    public const string Cancel = "";
    public const string List = "";
    public const string Download = "";
    public const string Document = "";
    public const string QrCode = "";
    public const string Ticket = "";
    public const string Clock = "";
    public const string Enable = "";
    public const string Disable = "";
    public const string Refresh = "";
    public const string Settings = "";
    public const string Info = "";
    public const string Success = "";
    public const string Warning = "";
    public const string Error = "";
    public const string Question = "";
    public const string CheckMark = "";
    public const string ChevronDown = "";
    public const string ChevronRight = "";
}
