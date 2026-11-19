namespace EtkinlikYonetim.Domain.Repositories;

/// <summary>
/// The clock that decisions on shared data use and that stored times are written with: which events are
/// past, whether a registration can still be made or cancelled, sign-in locks, and the times a
/// registration, cancellation or check-in happened. It is the database server's clock, so every computer
/// decides the same, also one whose own clock is wrong or was set back on purpose.
/// </summary>
public interface IDatabaseClock
{
    /// <summary>The current UTC time (<see cref="DateTimeKind.Utc"/>) of the database's clock.</summary>
    Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default);
}
