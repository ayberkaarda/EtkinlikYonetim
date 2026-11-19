using System.Runtime.InteropServices;
using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>
/// <c>EtkinlikYonetim.exe --create-setup-key</c>: the founder's separate step that issues a setup key for
/// the configured database, only while it has no admin. The key is written once to the console the
/// command was started from (or to its redirected output); the database keeps only its hash. No window
/// opens.
/// </summary>
internal static class SetupKeyCommand
{
    public const string Argument = "--create-setup-key";

    public static bool IsRequested(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Any(a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Initializes the database, issues a key and writes it to <paramref name="output"/>; returns the exit code.</summary>
    public static int Run(IServiceProvider services, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            services.GetRequiredService<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
            var key = services.GetRequiredService<ISetupService>().CreateSetupKeyAsync().GetAwaiter().GetResult();
            output.WriteLine("Kurulum anahtarı (yalnızca şimdi gösterilir; 24 saat geçerli, tek kullanımlık):");
            output.WriteLine(key);
            return 0;
        }
        catch (BusinessRuleException ex)
        {
            output.WriteLine(ex.Message);
            return 2;
        }
#pragma warning disable CA1031 // Command boundary: report without details (they may contain connection data) and exit.
        catch (Exception)
#pragma warning restore CA1031
        {
            output.WriteLine("Kurulum anahtarı üretilemedi: veritabanına bağlanılamadı veya veritabanı hazırlanamadı.");
            return 1;
        }
    }

    /// <summary>The console of the parent process (a WinExe has none of its own), or the redirected output.</summary>
    public static TextWriter OpenConsole()
    {
        _ = NativeMethods.AttachConsole(NativeMethods.AttachParentProcess);
        return new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
    }

    private static class NativeMethods
    {
        public const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(int processId);
    }
}
