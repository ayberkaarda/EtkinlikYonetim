using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI.Tests;

public sealed class ErrorReporterTests
{
    // A connection detail and a national id, as they could appear in a provider's exception message.
    private const string Secret = "Server=prod-sql-01;User Id=ayse; TC=10000000450";

    [Fact]
    public void FormatForLog_WritesTypesCodesAndMethodsOnly()
    {
        var exception = Capture();

        var log = ErrorReporter.FormatForLog(exception);

        Assert.Contains(typeof(InvalidOperationException).FullName!, log, StringComparison.Ordinal);
        Assert.Contains(typeof(TimeoutException).FullName!, log, StringComparison.Ordinal);
        Assert.Contains("HResult 0x", log, StringComparison.Ordinal);
        Assert.Contains(nameof(ThrowInner), log, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, log, StringComparison.Ordinal);
        Assert.DoesNotContain("prod-sql", log, StringComparison.Ordinal);
        Assert.DoesNotContain("10000000450", log, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs", log, StringComparison.Ordinal);
        Assert.DoesNotContain(":line", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_UnexpectedError_ShowsFixedMessageWithoutDetails()
    {
        var message = ErrorReporter.Describe(new InvalidOperationException(Secret));

        Assert.Equal(ErrorReporter.UnexpectedErrorMessage, message);
        Assert.DoesNotContain("10000000450", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_DatabaseFailure_ShowsTheDatabaseMessage()
    {
        var message = ErrorReporter.Describe(new InvalidOperationException("x", new Data.Persistence.DatabaseInitializationException()));

        Assert.StartsWith("Veritabanı hazırlanamadı.", message, StringComparison.Ordinal);
    }

    /// <summary>A lock that another change held too long: the user is told nothing changed and to try again, not to check the server.</summary>
    [Fact]
    public void Describe_BusyLock_AsksToTryAgain()
    {
        var message = ErrorReporter.Describe(new InvalidOperationException("x", new Data.Persistence.DatabaseBusyException()));

        Assert.Equal(ErrorReporter.BusyMessage, message);
        Assert.Contains("hiçbir değişiklik yapılmadı", message, StringComparison.Ordinal);
        Assert.Contains("tekrar deneyin", message, StringComparison.Ordinal);
    }

    private static InvalidOperationException Capture()
    {
        try
        {
            try
            {
                ThrowInner();
            }
            catch (TimeoutException inner)
            {
                throw new InvalidOperationException("Outer " + Secret, inner);
            }
        }
        catch (InvalidOperationException outer)
        {
            return outer;
        }

        throw new InvalidOperationException("Not reached.");
    }

    private static void ThrowInner() => throw new TimeoutException("Inner " + Secret);
}
