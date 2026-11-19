using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// A deterministic failure of the next transaction commit after <see cref="Arm"/>: everything the
/// transaction wrote, including the use of a setup key, has to roll back.
/// </summary>
internal sealed class CommitFailure : DbTransactionInterceptor
{
    private int _armed;
    private int _fired;

    public bool Fired => Volatile.Read(ref _fired) == 1;

    public void Arm() => Volatile.Write(ref _armed, 1);

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _armed, 0) == 1)
        {
            Volatile.Write(ref _fired, 1);
            throw new InvalidOperationException("Simulated commit failure.");
        }

        return ValueTask.FromResult(result);
    }
}
