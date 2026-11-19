using System.Drawing.Imaging;
using System.Globalization;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.Screenshots;

/// <summary>
/// Usage: <c>dotnet run --project tools/Screenshots -- [icons|screenshots|all] [--dark] [--out folder] [screen names...]</c>
/// (default: screenshots). "icons" rasterizes assets/logo.svg into assets/app.ico and assets/logo-256.png;
/// "screenshots" writes the README images to screenshots/ at 1x and 2x (with --dark, the dark palette to the
/// given folder, for checking it). No window is shown and the focus is never taken.
/// </summary>
internal static class Program
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        // Before any window handle exists: a DPI unaware process always measures at 96 DPI, which makes
        // the 1x images identical on every machine; the 2x images are scaled explicitly.
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0].ToLowerInvariant() : "screenshots";
        var options = args.SkipWhile(arg => arg == command).ToList();
        var root = FindRepositoryRoot();
        if (command is "icons" or "all")
        {
            WriteIcons(root);
        }

        if (command is "screenshots" or "all")
        {
            var dark = options.Remove("--dark");
            var output = Path.Combine(root, "screenshots");
            var outIndex = options.IndexOf("--out");
            if (outIndex >= 0 && outIndex + 1 < options.Count)
            {
                output = Path.GetFullPath(options[outIndex + 1]);
                options.RemoveRange(outIndex, 2);
            }

            return WriteScreenshots(output, dark ? ThemePalette.Dark : ThemePalette.Light, options.ToHashSet(StringComparer.Ordinal));
        }

        return command is "icons" ? 0 : Usage();
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: EtkinlikYonetim.Screenshots [icons|screenshots|all] [--dark] [--out folder] [screen names...]");
        return 2;
    }

    private static void WriteIcons(string root)
    {
        var svg = Path.Combine(root, "assets", "logo.svg");
        var frames = IconSizes.Select(size => SvgRasterizer.Render(svg, size)).ToList();
        try
        {
            IconWriter.Write(Path.Combine(root, "assets", "app.ico"), frames);
            frames[^1].Save(Path.Combine(root, "assets", "logo-256.png"), ImageFormat.Png);
        }
        finally
        {
            frames.ForEach(frame => frame.Dispose());
        }

        Console.WriteLine("assets/app.ico, assets/logo-256.png");
    }

    /// <param name="only">Screen names to write; all screens when empty.</param>
    private static int WriteScreenshots(string output, ThemePalette palette, HashSet<string> only)
    {
        var culture = CultureInfo.GetCultureInfo("tr-TR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // A message window would be visible: record the request and fail the run instead.
        var unexpected = new List<string>();
        Dialogs.Override = (text, caption, _, _) =>
        {
            unexpected.Add(caption + ": " + text);
            return DialogResult.Yes;
        };

        foreach (var scale in new[] { 1F, 2F })
        {
            foreach (var (name, build) in Screens.All.Where(screen => only.Count == 0 || only.Contains(screen.Name)))
            {
                // The size a screen opens at: built at 1x with a hidden window, so it fits itself to its content.
                Size logical;
                Theme.Use(palette, 1F);
                using (var probe = build(new SyntheticFestival()))
                {
                    _ = probe.Handle;
                    logical = probe.ClientSize;
                }

                Theme.Use(palette, scale);
                using var form = build(new SyntheticFestival());
                using var image = OffscreenCapture.Capture(form, logical, scale);
                var file = scale > 1 ? $"{name}-2x.png" : $"{name}.png";
                OffscreenCapture.Save(image, Path.Combine(output, file));
                Console.WriteLine($"{file} ({image.Width}x{image.Height})");
            }
        }

        Theme.Use(ThemePalette.Light);
        if (unexpected.Count > 0)
        {
            Console.Error.WriteLine("A screen tried to open a message window:");
            unexpected.ForEach(Console.Error.WriteLine);
            return 1;
        }

        return 0;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EtkinlikYonetim.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("EtkinlikYonetim.sln was not found above the tool's folder.");
    }
}
