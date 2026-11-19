using EtkinlikYonetim.Data;
using Microsoft.Extensions.Configuration;

namespace EtkinlikYonetim.UI.Tests;

public sealed class ConfigurationTests
{
    private const string ExampleFileName = "appsettings.example.json";

    [Fact]
    public void ExampleConfiguration_LoadsWithNamedInstance()
    {
        Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, ExampleFileName)),
            $"{ExampleFileName} was not copied next to the tests.");

        // Same loader the application uses for appsettings.json; a malformed file throws here.
        var configuration = new ConfigurationBuilder()
            .AddSettingsFile(AppContext.BaseDirectory, ExampleFileName)
            .Build();

        var connectionString = AppConfiguration.ResolveConnectionString(configuration);

        Assert.NotNull(connectionString);
        Assert.Contains(@"Server=localhost\SQLEXPRESS;", connectionString, StringComparison.Ordinal);
        Assert.Contains("Database=EtkinlikYonetim;", connectionString, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\\", connectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentVariable_OverridesSettingsFile()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:EtkinlikYonetim"] = "Server=from-file;",
                [DataServiceCollectionExtensions.ConnectionEnvironmentVariable] = "Server=from-environment;",
            })
            .Build();

        Assert.Equal("ETKINLIK_CONNECTION", DataServiceCollectionExtensions.ConnectionEnvironmentVariable);
        Assert.Equal("Server=from-environment;", AppConfiguration.ResolveConnectionString(configuration));
    }

    [Fact]
    public void SettingsFile_IsUsedWithoutEnvironmentVariable()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:EtkinlikYonetim"] = "Server=from-file;" })
            .Build();

        Assert.Equal("Server=from-file;", AppConfiguration.ResolveConnectionString(configuration));
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("0", null, false)]
    [InlineData("evet", null, false)]
    [InlineData(null, "false", false)]
    [InlineData("1", null, true)]
    [InlineData(" TRUE ", null, true)]
    [InlineData(null, "true", true)]
    public void DemoData_IsOnlyUsedWhenAskedForExplicitly(string? environment, string? setting, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataServiceCollectionExtensions.DemoEnvironmentVariable] = environment,
                [AppConfiguration.DemoSettingKey] = setting,
            })
            .Build();

        Assert.Equal("ETKINLIK_DEMO", DataServiceCollectionExtensions.DemoEnvironmentVariable);
        Assert.Equal(expected, AppConfiguration.IsDemoRequested(configuration));
    }

    [Fact]
    public void ExampleConfiguration_LeavesDemoDataOff()
    {
        var configuration = new ConfigurationBuilder()
            .AddSettingsFile(AppContext.BaseDirectory, ExampleFileName)
            .Build();

        Assert.False(AppConfiguration.IsDemoRequested(configuration));
    }
}
