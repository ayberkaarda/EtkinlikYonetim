using System.Globalization;
using System.Text.RegularExpressions;
using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using EtkinlikYonetim.UI.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>What reaches the log file (KVKK). No window is created by these tests.</summary>
public sealed partial class LoggingTests : IDisposable
{
    // Personal data and secrets as they can appear in a sign-in, a setup, a participant or an exception message.
    private const string UserName = "denizornek";
    private const string Password = "Gizli.Sifre1";
    private const string NationalId = "10000000450";
    private const string FullName = "Deniz Örnek";
    private const string Email = "deniz.ornek@example.com";
    private const string SetupKey = "9F3A7C2E5B1D4F6A8C0E2B4D6F8A1C3E";
    private const string EventName = "Bahar Konseri";

    // Wednesday 5 March 2031, 10:10 UTC: the year cannot appear in a log timestamp written today.
    private static readonly DateTimeOffset Now = new(2031, 3, 5, 10, 10, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "etkinlikyonetim-log-test-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void CreateLogger_WritesADailyFileInTheGivenDirectory()
    {
        using (var logger = AppLogging.CreateLogger(_directory))
        {
            logger.Information("probe");
        }

        var file = Assert.Single(Directory.GetFiles(_directory));
        Assert.Matches(@"etkinlikyonetim-\d{8}\.log$", file);
        Assert.Contains("probe", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultDirectory_IsUnderLocalApplicationData()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(root, AppLogging.DefaultDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("EtkinlikYonetim", "logs"), AppLogging.DefaultDirectory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Files dated before the last 30 days are deleted when the logger opens today's file.</summary>
    [Fact]
    public void Retention_DeletesFilesOlderThanThirtyDaysAndKeepsRecentOnes()
    {
        int[] ages = [1, 2, 15, 29, 30, 31, 45, 60, 400];
        Directory.CreateDirectory(_directory);
        foreach (var age in ages)
        {
            File.WriteAllText(DatedFile(age), "old");
        }

        var unrelated = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(unrelated, "not a log file");

        using (var logger = AppLogging.CreateLogger(_directory))
        {
            logger.Information("probe");
        }

        Assert.Equal(30, AppLogging.RetainedDays);
        Assert.All(ages.Where(a => a < 30), age => Assert.True(File.Exists(DatedFile(age)), $"The file of {age} days ago was deleted."));
        Assert.All(ages.Where(a => a >= 30), age => Assert.False(File.Exists(DatedFile(age)), $"The file of {age} days ago was kept."));
        Assert.True(File.Exists(DatedFile(0)));
        Assert.True(File.Exists(unrelated));
    }

    /// <summary>
    /// More than 30 recent files (several per day, as size or restart sequences produce) are cut to the
    /// newest 30, today's file included.
    /// </summary>
    [Fact]
    public void Retention_KeepsAtMostThirtyFilesNewestFirst()
    {
        Directory.CreateDirectory(_directory);
        var created = new List<string>();
        for (var age = 1; age <= 10; age++)
        {
            created.Add(DatedFile(age));
            for (var sequence = 1; sequence <= 3; sequence++)
            {
                created.Add(DatedFile(age, sequence));
            }
        }

        foreach (var file in created)
        {
            File.WriteAllText(file, "old");
        }

        using (var logger = AppLogging.CreateLogger(_directory))
        {
            logger.Information("probe");
        }

        // Newest first: day 1 sequence 3, 2, 1, base, then day 2 and so on.
        var newestFirst = created.OrderBy(AgeOf).ThenByDescending(SequenceOf).ToList();
        var remaining = Directory.GetFiles(_directory, "etkinlikyonetim-*.log");
        Assert.Equal(30, AppLogging.RetainedFileCount);
        Assert.Equal(AppLogging.RetainedFileCount, remaining.Length);
        Assert.All(newestFirst.Take(AppLogging.RetainedFileCount - 1), f => Assert.True(File.Exists(f), Path.GetFileName(f) + " was deleted."));
        Assert.All(newestFirst.Skip(AppLogging.RetainedFileCount - 1), f => Assert.False(File.Exists(f), Path.GetFileName(f) + " was kept."));
    }

    [Fact]
    public void Record_UnexpectedError_KeepsTypesCodesAndMethodsButNoMessageOrPersonalData()
    {
        var exception = Throw(new InvalidOperationException(
            $"Kayıt yapılamadı: {FullName} {Email} T.C. {NationalId} şifre {Password} anahtar {SetupKey}",
            new TimeoutException($"{UserName} {EventName} 05.03.2031")));

        var text = WriteAndRead(logger => ErrorReporter.Record(logger, exception));

        Assert.Contains("[ERR] Operation failed", text, StringComparison.Ordinal);
        Assert.Contains(typeof(InvalidOperationException).FullName!, text, StringComparison.Ordinal);
        Assert.Contains(typeof(TimeoutException).FullName!, text, StringComparison.Ordinal);
        Assert.Contains("HResult 0x", text, StringComparison.Ordinal);
        Assert.Contains(nameof(LoggingTests) + "." + nameof(Throw), text, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Kayıt yapılamadı", text, StringComparison.Ordinal);
        AssertNoPersonalData(text);
    }

    [Fact]
    public void Record_RefusedOperation_LogsTheTypeButNotTheMessage()
    {
        var text = WriteAndRead(logger =>
        {
            ErrorReporter.Record(logger, new BusinessRuleException($"{NationalId} {FullName} {Email}"));
            ErrorReporter.Record(logger, new AccessDeniedException($"{UserName} {SetupKey}"));
        });

        Assert.Contains("[WRN] Operation refused: BusinessRuleException", text, StringComparison.Ordinal);
        Assert.Contains("[WRN] Operation refused: AccessDeniedException", text, StringComparison.Ordinal);
        AssertNoPersonalData(text);
    }

    [Fact]
    public void AuditLog_WritesOnlyTheCodeAndTheRole()
    {
        var text = WriteAndRead(logger =>
        {
            var audit = new SerilogAuditLog(logger);
            foreach (var auditEvent in Enum.GetValues<AuditEvent>())
            {
                audit.Record(auditEvent, UserRole.User);
                audit.Record(auditEvent, UserRole.Admin);
                audit.Record(auditEvent, null);
            }
        });

        Assert.Contains("[INF] Audit auth.sign_in.failed none", text, StringComparison.Ordinal);
        Assert.Contains("[INF] Audit registration.cancelled user", text, StringComparison.Ordinal);
        Assert.Contains("[INF] Audit event.created admin", text, StringComparison.Ordinal);
        Assert.All(Lines(text), line => Assert.Matches(AuditLine(), line));
    }

    /// <summary>
    /// Sign-up, failed and successful sign-ins up to a lock, a refused setup key, a participant with a
    /// national id, a registration with a ticket, a denied and a successful cancellation: the file keeps
    /// only audit codes, roles and refusal types.
    /// </summary>
    [Fact]
    public async Task FullSession_LeavesOnlyCodesAndTypesInTheLog()
    {
        using var logger = AppLogging.CreateLogger(_directory);
        var hasher = new PasswordHasher(iterations: 1_000);
        var account = new AccountCredentials(7, UserName, UserRole.User, hasher.Hash(Password), PasswordFormat.Pbkdf2, 0, null);
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.UserNameExistsAsync(UserName, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).ReturnsAsync(7);
        users.Setup(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>())).ReturnsAsync(() => account);
        users.Setup(u => u.RecordFailedLoginAsync(7, It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, DateTime, CancellationToken>((_, max, end, _) =>
                account = account.FailedLoginCount + 1 >= max
                    ? account with { FailedLoginCount = 0, LockoutEnd = end }
                    : account with { FailedLoginCount = account.FailedLoginCount + 1 })
            .Returns(Task.CompletedTask);
        users.Setup(u => u.RecordSuccessfulLoginAsync(It.IsAny<AccountCredentials>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        users.Setup(u => u.AddFirstAdminAsync(It.IsAny<User>(), It.IsAny<SetupKeyAttempt?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirstAdminResult(FirstAdminStatus.NotAuthorized, 0));
        users.Setup(u => u.GetSessionStateAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SessionState(UserRole.User, false));
        var day = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(10);
        var events = new Mock<IEventRepository>();
        events.Setup(e => e.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new EventSummary(1, EventName, day, 10, 0));
        var stored = new List<ParticipantRecord>();
        var participants = new Mock<IParticipantRepository>();
        participants.Setup(p => p.FindByNationalIdAsync(NationalId, 7, It.IsAny<CancellationToken>())).ReturnsAsync(() => [.. stored]);
        participants.Setup(p => p.AddAsync(It.IsAny<Participant>(), It.IsAny<CancellationToken>()))
            .Callback<Participant, CancellationToken>((p, _) => stored.Add(new ParticipantRecord(3, p.NationalId, p.FullName, null, p.OwnerUserId)))
            .ReturnsAsync(3);
        var registrations = new Mock<IRegistrationRepository>();
        registrations.Setup(r => r.AddAsync(3, 1, It.IsAny<DateOnly>(), It.IsAny<DateTime>(), It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()))
            .Returns((int _, int _, DateOnly _, DateTime _, Func<string> newTicketCode, CancellationToken _) =>
            {
                newTicketCode();
                return Task.FromResult(new RegistrationOutcome(5, RegistrationStatus.Active, null));
            });
        registrations.Setup(r => r.GetOwnershipAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationOwnership(5, 1, day, 7, RegistrationStatus.Active));
        registrations.Setup(r => r.CancelAsync(5, 7, It.IsAny<DateOnly>(), It.IsAny<DateTime>(), It.IsAny<Func<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(CancellationOutcome.Cancelled);

        var clock = new FakeTimeProvider(Now);
        await using var provider = new ServiceCollection()
            .AddSingleton<IAuditLog>(new SerilogAuditLog(logger))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IPasswordHasher>(hasher)
            .AddSingleton(new SetupKeyThrottle(TimeSpan.Zero))
            .AddSingleton(new SetupKeyEnvironment(null))
            .AddSingleton(users.Object)
            .AddSingleton(events.Object)
            .AddSingleton(participants.Object)
            .AddSingleton(registrations.Object)
            .AddBusinessServices()
            .BuildServiceProvider();
        var auth = provider.GetRequiredService<IAuthService>();

        async Task Attempt(Func<Task> action)
        {
            try
            {
                await action();
            }
#pragma warning disable CA1031 // Every failure goes to the log, as the forms do.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ErrorReporter.Record(logger, ex);
            }
        }

        await auth.RegisterAsync(UserName, Password, Password, Ct);
        for (var i = 0; i < LoginPolicy.MaxFailedAttempts; i++)
        {
            await Attempt(() => auth.SignInAsync(UserName, "yanlis-" + Password, Ct));
        }

        await Attempt(() => auth.SignInAsync(UserName, Password, Ct)); // locked
        clock.Advance(LoginPolicy.LockoutDuration);
        var session = await auth.SignInAsync(UserName, Password, Ct);
        await Attempt(() => provider.GetRequiredService<ISetupService>().CreateFirstAdminAsync(SetupKey, "kurucu", Password, Password, Ct));
        await provider.GetRequiredService<IParticipantService>().AddAsync(session, NationalId, FullName, Email, Ct);
        var registrationService = provider.GetRequiredService<IRegistrationService>();
        var receipt = await registrationService.RegisterAsync(session, 1, NationalId, Ct);
        Assert.NotNull(receipt.TicketCode);
        await Attempt(() => registrationService.CancelAsync(new UserSession(9, "baska", UserRole.User), 5, Ct)); // not the owner
        await Attempt(() => provider.GetRequiredService<IEventService>().CreateAsync(session, EventName, Now.UtcDateTime, 10, Ct)); // not an admin
        await registrationService.CancelAsync(session, 5, Ct);
        logger.Dispose();

        var text = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory)));
        Assert.Contains("Audit auth.account.registered user", text, StringComparison.Ordinal);
        // The refusal of the locked account is counted as one more failure, not as a lock.
        Assert.Equal(LoginPolicy.MaxFailedAttempts + 1, Regex.Count(text, "Audit auth.sign_in.failed none"));
        Assert.Contains("Audit auth.sign_in.succeeded user", text, StringComparison.Ordinal);
        Assert.Contains("Audit setup.key.refused none", text, StringComparison.Ordinal);
        Assert.Contains("Audit participant.added user", text, StringComparison.Ordinal);
        Assert.Contains("Audit registration.created user", text, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(text, "Audit access.denied user"));
        Assert.Contains("Audit registration.cancelled user", text, StringComparison.Ordinal);
        Assert.DoesNotContain("lock", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(receipt.TicketCode, text, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.TicketCode[4..], text, StringComparison.Ordinal);
        Assert.All(Lines(text), line => Assert.Matches(AllowedLine(), line));
        AssertNoPersonalData(text);
    }

    /// <summary>
    /// The log file is readable by the local Windows user, who knows when they made an attempt. A wrong
    /// password for an unknown, an unlocked and a locked account (an admin's, too) must therefore leave
    /// identical entries and show identical messages, or the file would reveal which accounts exist,
    /// which are locked and which belong to an admin.
    /// </summary>
    [Fact]
    public async Task SignInFailures_DoNotDiscloseAccountLockOrRoleInTheLocalLog()
    {
        var hasher = new PasswordHasher(iterations: 1_000);
        var accounts = new Dictionary<string, AccountCredentials>(StringComparer.Ordinal)
        {
            ["acik"] = new(1, "acik", UserRole.User, hasher.Hash(Password), PasswordFormat.Pbkdf2, 0, null),
            ["kilitli"] = new(2, "kilitli", UserRole.User, hasher.Hash(Password), PasswordFormat.Pbkdf2, 0, Now.UtcDateTime.AddMinutes(10)),
            ["yonetici"] = new(3, "yonetici", UserRole.Admin, hasher.Hash(Password), PasswordFormat.Pbkdf2, 0, null),
        };

        var outcomes = new List<(string Message, string[] Entries)>();
        foreach (var name in new[] { "bilinmeyen", "acik", "kilitli", "yonetici" })
        {
            var directory = Path.Combine(_directory, name);
            string message;
            using (var logger = AppLogging.CreateLogger(directory))
            {
                var users = new Mock<IUserRepository>();
                users.Setup(u => u.FindByUserNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string n, CancellationToken _) => accounts.GetValueOrDefault(n));
                users.Setup(u => u.RecordFailedLoginAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                await using var provider = new ServiceCollection()
                    .AddSingleton<IAuditLog>(new SerilogAuditLog(logger))
                    .AddSingleton<TimeProvider>(new FakeTimeProvider(Now))
                    .AddSingleton<IPasswordHasher>(hasher)
                    .AddSingleton(new SetupKeyEnvironment(null))
                    .AddSingleton(users.Object)
                    .AddSingleton(Mock.Of<IEventRepository>())
                    .AddSingleton(Mock.Of<IParticipantRepository>())
                    .AddSingleton(Mock.Of<IRegistrationRepository>())
                    .AddBusinessServices()
                    .BuildServiceProvider();

                var refused = await Assert.ThrowsAsync<BusinessRuleException>(
                    () => provider.GetRequiredService<IAuthService>().SignInAsync(name, "yanlis-" + Password, Ct));
                ErrorReporter.Record(logger, refused); // as the sign-in form does
                message = refused.Message;
            }

            var entries = Lines(File.ReadAllText(Assert.Single(Directory.GetFiles(directory))))
                .Select(line => StampPattern().Replace(line, string.Empty))
                .ToArray();
            outcomes.Add((message, entries));
        }

        Assert.Equal(["[INF] Audit auth.sign_in.failed none", "[WRN] Operation refused: BusinessRuleException"], outcomes[0].Entries);
        Assert.All(outcomes, o => Assert.Equal(outcomes[0].Entries, o.Entries));
        Assert.All(outcomes, o => Assert.Equal(AuthService.CredentialsInvalid, o.Message));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static void AssertNoPersonalData(string text)
    {
        Assert.DoesNotContain(NationalId, text, StringComparison.Ordinal);
        Assert.DoesNotContain(NationalId[..5], text, StringComparison.Ordinal); // not even a masked prefix
        Assert.DoesNotContain("Deniz", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Örnek", text, StringComparison.Ordinal);
        Assert.DoesNotContain(UserName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Email, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Sifre", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("şifre", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SetupKey, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SetupKey[..8], text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kurucu", text, StringComparison.Ordinal);
        Assert.DoesNotContain(EventName, text, StringComparison.Ordinal);
        Assert.DoesNotContain("2031", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PBKDF2", text, StringComparison.Ordinal);
    }

    private static string[] Lines(string text) =>
        text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private const string Stamp = @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} ";

    [GeneratedRegex(Stamp)]
    private static partial Regex StampPattern();

    [GeneratedRegex(Stamp + @"\[INF\] Audit [a-z_]+(\.[a-z_]+)+ (admin|user|none)$")]
    private static partial Regex AuditLine();

    [GeneratedRegex(Stamp + @"(\[INF\] Audit [a-z_]+(\.[a-z_]+)+ (admin|user|none)|\[WRN\] Operation refused: (BusinessRuleException|AccessDeniedException))$")]
    private static partial Regex AllowedLine();

    private string DatedFile(int age, int sequence = 0)
    {
        var day = DateTime.Today.AddDays(-age).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var suffix = sequence == 0 ? string.Empty : "_" + sequence.ToString("000", CultureInfo.InvariantCulture);
        return Path.Combine(_directory, $"etkinlikyonetim-{day}{suffix}.log");
    }

    private static int AgeOf(string file)
    {
        var day = DateTime.ParseExact(Path.GetFileName(file).Substring("etkinlikyonetim-".Length, 8), "yyyyMMdd", CultureInfo.InvariantCulture);
        return (DateTime.Today - day).Days;
    }

    private static int SequenceOf(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var underscore = name.LastIndexOf('_');
        return underscore < 0 ? 0 : int.Parse(name[(underscore + 1)..], CultureInfo.InvariantCulture);
    }

    private string WriteAndRead(Action<Serilog.ILogger> write)
    {
        using (var logger = AppLogging.CreateLogger(_directory))
        {
            write(logger);
        }

        return File.ReadAllText(Assert.Single(Directory.GetFiles(_directory)));
    }

    private static Exception Throw(Exception exception)
    {
        try
        {
            throw exception;
        }
#pragma warning disable CA1031 // Test helper captures the stack trace.
        catch (Exception caught)
#pragma warning restore CA1031
        {
            return caught;
        }
    }
}
