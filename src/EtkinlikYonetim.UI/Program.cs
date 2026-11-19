using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data;
using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EtkinlikYonetim.UI
{
    internal static class Program
    {
        internal const string ConnectionStringName = "EtkinlikYonetim";

        /// <summary>
        /// Application entry point and composition root. The application never creates setup keys;
        /// only the database owner does, with docs/issue-setup-key.sql.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Log.Logger = AppLogging.CreateLogger(AppLogging.DefaultDirectory);
            try
            {
                Log.Information("Application started");
                Run();
            }
            finally
            {
                Log.Information("Application stopped");
                Log.CloseAndFlush();
            }
        }

        private static void Run()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ErrorReporter.Show(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.IsTerminating)
                {
                    Log.Fatal("Unhandled exception terminates the application");
                }

                ErrorReporter.Show(e.ExceptionObject as Exception);
                if (e.IsTerminating)
                {
                    Log.CloseAndFlush();
                }
            };

            ApplicationConfiguration.Initialize();

            var configuration = BuildConfiguration();
            ApplyTheme(AppConfiguration.ResolveTheme(configuration));
            var connectionString = AppConfiguration.ResolveConnectionString(configuration);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Log.Warning("No database connection string is configured");
                Dialogs.Show(
                    null,
                    "Veritabanı bağlantı ayarı bulunamadı.\n\n"
                    + "appsettings.example.json dosyasını appsettings.json olarak kopyalayıp bağlantı bilgisini girin "
                    + "ya da ETKINLIK_CONNECTION ortam değişkenini tanımlayın.",
                    AppText.Title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var options = new DataAccessOptions { SeedDemoData = AppConfiguration.IsDemoRequested(configuration) };
            if (options.SeedDemoData)
            {
                Log.Warning("Demo mode is on: demo accounts with published passwords are added to an empty database");
            }

            using var serviceProvider = ConfigureServices(connectionString, options);
            bool setupRequired;
            bool adminMissing;
            try
            {
                // No synchronization context exists yet, so blocking here cannot deadlock.
                serviceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
                var setupService = serviceProvider.GetRequiredService<ISetupService>();
                setupRequired = setupService.IsSetupRequiredAsync().GetAwaiter().GetResult();
                adminMissing = setupRequired || setupService.IsAdminMissingAsync().GetAwaiter().GetResult();
            }
#pragma warning disable CA1031 // Startup boundary: report and exit instead of crashing.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ErrorReporter.Show(ex);
                return;
            }

            var formFactory = serviceProvider.GetRequiredService<IFormFactory>();

            // While the application runs, Windows may switch high contrast or the app mode; open screens follow.
            using var themeWatcher = new ThemeWatcher(
                new SystemPreferenceSource(),
                new WindowsFormsSynchronizationContext(),
                () => SystemInformation.HighContrast,
                Theme.SystemPrefersDark);

            // On an empty database the first admin is created with a password of the installer's own
            // choosing; there is no default account. Closing the setup ends the application. A database
            // with accounts but no admin keeps its sign-in, which offers the setup with the local setup key.
            if (setupRequired)
            {
                using var setup = formFactory.Create<IlkKurulumForm>();
                if (setup.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
            }

            using var signIn = formFactory.Create<GirisYapForm>();
            signIn.OfferAdminSetup = adminMissing && !setupRequired;
            if (setupRequired)
            {
                signIn.ShowNotice(GirisYapForm.AdminCreatedNotice);
            }

            Application.Run(signIn);
        }

        /// <summary>Chooses the palette; in dark mode the native parts of the controls are switched to dark too.</summary>
        private static void ApplyTheme(ThemeMode mode)
        {
            var highContrast = SystemInformation.HighContrast;
            Theme.Initialize(mode, highContrast, Theme.SystemPrefersDark());
            if (Theme.Current.IsDark && !highContrast)
            {
#pragma warning disable WFO5001 // Dark mode of the native controls is marked experimental in Windows Forms.
                Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
            }
        }

        private static IConfiguration BuildConfiguration() =>
            new ConfigurationBuilder()
                .AddSettingsFile(AppContext.BaseDirectory, AppConfiguration.SettingsFileName)
                .AddUserSecrets(typeof(Program).Assembly, optional: true)
                .AddEnvironmentVariables()
                .Build();

        private static ServiceProvider ConfigureServices(string connectionString, DataAccessOptions options)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAuditLog>(new SerilogAuditLog(Log.Logger));
            services.AddSqlServerDataAccess(connectionString, options);
            services.AddBusinessServices();
            services.AddSingleton<IFormFactory, FormFactory>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        }
    }

    /// <summary>How the application reads its settings.</summary>
    internal static class AppConfiguration
    {
        public const string SettingsFileName = "appsettings.json";

        /// <summary>Settings key of the demonstration data switch.</summary>
        public const string DemoSettingKey = "Seed:Demo";

        /// <summary>Settings key of the palette: Light (default), Dark or System.</summary>
        public const string ThemeKey = "Appearance:Theme";

        /// <summary>The <c>Appearance:Theme</c> setting (Light, Dark or System); anything else falls back to Light.</summary>
        public static ThemeMode ResolveTheme(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var value = configuration[ThemeKey]?.Trim();

            // Only the names are accepted; Enum.TryParse would also take numbers.
            return value is { Length: > 0 } && value.All(char.IsAsciiLetter) && Enum.TryParse<ThemeMode>(value, ignoreCase: true, out var mode)
                ? mode
                : ThemeMode.Light;
        }

        /// <summary>Adds an optional JSON settings file located in <paramref name="basePath"/>.</summary>
        public static IConfigurationBuilder AddSettingsFile(this IConfigurationBuilder builder, string basePath, string fileName) =>
            builder
                .SetBasePath(basePath)
                .AddJsonFile(fileName, optional: true, reloadOnChange: false);

        /// <summary>
        /// Resolution order (last wins): appsettings.json, user secrets, ConnectionStrings__EtkinlikYonetim
        /// environment variable, ETKINLIK_CONNECTION environment variable.
        /// </summary>
        public static string? ResolveConnectionString(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var fromEnvironment = configuration[DataServiceCollectionExtensions.ConnectionEnvironmentVariable];
            return string.IsNullOrWhiteSpace(fromEnvironment)
                ? configuration.GetConnectionString(Program.ConnectionStringName)
                : fromEnvironment;
        }

        /// <summary>
        /// Whether the demonstration data was asked for, through ETKINLIK_DEMO ("1" or "true") or the
        /// Seed:Demo setting. Anything else, including a missing or malformed value, means no.
        /// </summary>
        public static bool IsDemoRequested(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            return IsYes(configuration[DataServiceCollectionExtensions.DemoEnvironmentVariable])
                || IsYes(configuration[DemoSettingKey]);
        }

        private static bool IsYes(string? value) =>
            value is not null
            && (value.Trim() == "1" || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
    }
}
