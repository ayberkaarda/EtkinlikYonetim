using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only. Reads the connection string from ETKINLIK_CONNECTION and falls back
/// to a local SQL Server Express instance.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EventDbContext>
{
    private const string FallbackConnectionString =
        "Server=localhost\\SQLEXPRESS;Database=EtkinlikYonetim;Trusted_Connection=True;TrustServerCertificate=True;";

    public EventDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(DataServiceCollectionExtensions.ConnectionEnvironmentVariable);
        var options = new DbContextOptionsBuilder<EventDbContext>()
            .UseSqlServer(string.IsNullOrWhiteSpace(connectionString) ? FallbackConnectionString : connectionString)
            .Options;
        return new EventDbContext(options);
    }
}
