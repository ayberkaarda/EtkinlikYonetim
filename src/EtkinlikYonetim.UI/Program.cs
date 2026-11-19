using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data;
using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.UI.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EtkinlikYonetim.UI
{
    internal static class Program
    {
        internal const string ConnectionStringName = "EtkinlikYonetim";

        /// <summary>Application entry point and composition root.</summary>
        [STAThread]
        private static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ErrorReporter.Show(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorReporter.Show(e.ExceptionObject as Exception);

            ApplicationConfiguration.Initialize();

            var configuration = BuildConfiguration();
            var connectionString = AppConfiguration.ResolveConnectionString(configuration);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
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

            var signIn = formFactory.Create<GirisYapForm>();
            signIn.OfferAdminSetup = adminMissing && !setupRequired;
            Application.Run(signIn);
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
