using EtkinlikYonetim.Business;
using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>The --create-setup-key command writes the issued key once to its output and opens no window.</summary>
public sealed class SetupKeyCommandTests
{
    [Theory]
    [InlineData(new[] { "--create-setup-key" }, true)]
    [InlineData(new[] { "--CREATE-SETUP-KEY" }, true)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--create-setup" }, false)]
    public void IsRequested_OnlyForTheExactArgument(string[] args, bool expected) =>
        Assert.Equal(expected, SetupKeyCommand.IsRequested(args));

    [Fact]
    public void Run_InitializesTheDatabaseAndWritesTheKey()
    {
        var initializer = new CountingInitializer();
        using var services = Services(initializer, new PendingSetupService());
        using var output = new StringWriter();

        Assert.Equal(0, SetupKeyCommand.Run(services, output));

        Assert.Equal(1, initializer.Calls);
        Assert.Contains("ABCDE-FGHJK-MNPQR-STVWX", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_RefusedOrFailed_WritesOnlyTheReason()
    {
        using var refused = Services(new CountingInitializer(), new RefusingSetupService());
        using var refusedOutput = new StringWriter();
        using var failed = Services(new CountingInitializer(fail: true), new PendingSetupService());
        using var failedOutput = new StringWriter();

        Assert.Equal(2, SetupKeyCommand.Run(refused, refusedOutput));
        Assert.Equal(1, SetupKeyCommand.Run(failed, failedOutput));

        Assert.Contains("yönetici hesabı var", refusedOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Server=", failedOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ABCDE", failedOutput.ToString(), StringComparison.Ordinal);
    }

    private static ServiceProvider Services(IDatabaseInitializer initializer, Business.Services.ISetupService setup) =>
        new ServiceCollection().AddSingleton(initializer).AddSingleton(setup).BuildServiceProvider();

    private sealed class CountingInitializer(bool fail = false) : IDatabaseInitializer
    {
        public int Calls { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return fail ? Task.FromException(new InvalidOperationException("Server=secret;Password=x")) : Task.CompletedTask;
        }
    }

    private sealed class RefusingSetupService : Business.Services.ISetupService
    {
        public Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> IsAdminMissingAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<string> CreateSetupKeyAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new BusinessRuleException("Bu veritabanında bir yönetici hesabı var; kurulum anahtarı üretilemez."));

        public Task<Business.Services.UserSession> CreateFirstAdminAsync(string? setupKey, string? userName, string? password, string? passwordRepeat, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
