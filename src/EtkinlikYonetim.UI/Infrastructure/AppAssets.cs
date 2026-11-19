namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>Application wide texts.</summary>
internal static class AppText
{
    /// <summary>Neutral product name used in window titles and message boxes.</summary>
    public const string Title = "Etkinlik Yönetim";
}

/// <summary>
/// The application's own artwork (assets/), embedded in the assembly. The images are shared by all
/// forms and live for the whole process, so forms must not dispose them.
/// </summary>
internal static class AppAssets
{
    private static readonly Lazy<Image> LazyLogo = new(() => LoadImage("logo-256.png"));
    private static readonly Lazy<Image> LazyBackground = new(() => LoadImage("background.png"));
    private static readonly Lazy<Icon> LazyIcon = new(LoadIcon);

    /// <summary>The logo at 256 px, for painting it at any size.</summary>
    public static Image Logo => LazyLogo.Value;

    public static Image Background => LazyBackground.Value;

    public static Icon Icon => LazyIcon.Value;

    private static Bitmap LoadImage(string name)
    {
        // Image.FromStream needs the stream for the lifetime of the image; a copy detaches it.
        using var stream = Open(name);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static Icon LoadIcon()
    {
        using var stream = Open("app.ico");
        return new Icon(stream);
    }

    private static Stream Open(string name) =>
        typeof(AppAssets).Assembly.GetManifestResourceStream("EtkinlikYonetim.UI.Assets." + name)
            ?? throw new InvalidOperationException($"Embedded asset '{name}' is missing.");
}
