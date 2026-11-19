using EtkinlikYonetim.Business;
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

            var connectionString = AppConfiguration.ResolveConnectionString(BuildConfiguration());
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

            using var serviceProvider = ConfigureServices(connectionString);

            try
            {
                // No synchronization context exists yet, so blocking here cannot deadlock.
                serviceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
            }
#pragma warning disable CA1031 // Startup boundary: report and exit instead of crashing.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ErrorReporter.Show(ex);
                return;
            }

            var formFactory = serviceProvider.GetRequiredService<IFormFactory>();
            Application.Run(formFactory.Create<GirisYapForm>());
        }

        private static IConfiguration BuildConfiguration() =>
            new ConfigurationBuilder()
                .AddSettingsFile(AppContext.BaseDirectory, AppConfiguration.SettingsFileName)
                .AddUserSecrets(typeof(Program).Assembly, optional: true)
                .AddEnvironmentVariables()
                .Build();

        private static ServiceProvider ConfigureServices(string connectionString)
        {
            var services = new ServiceCollection();
            services.AddSqlServerDataAccess(connectionString);
            services.AddBusinessServices();
            services.AddSingleton<IFormFactory, FormFactory>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        }
    }

    /// <summary>How the application reads its settings.</summary>
    internal static class AppConfiguration
    {
        public const string SettingsFileName = "appsettings.json";

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
    }
}
