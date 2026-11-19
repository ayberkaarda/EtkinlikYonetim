using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// A deterministic write failure: while armed, the next command that inserts into
/// <see cref="Table"/> fails, after everything before it in the same transaction has run.
/// </summary>
internal sealed class FailingInsertInterceptor(string table) : DbCommandInterceptor
{
    private int _armed;

    public string Table { get; } = table;

    public int Failures { get; private set; }

    /// <summary>Makes the next insert into <see cref="Table"/> fail once.</summary>
    public void FailNextInsert() => Interlocked.Exchange(ref _armed, 1);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfArmed(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfArmed(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void ThrowIfArmed(DbCommand command)
    {
        if (command.CommandText.Contains("INSERT INTO \"" + Table + "\"", StringComparison.Ordinal)
            && Interlocked.Exchange(ref _armed, 0) == 1)
        {
            Failures++;
            throw new InvalidOperationException("Simulated write failure.");
        }
    }
}