using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.UI.Tests.Support;

/// <summary>A database clock that reads <paramref name="time"/>, as on a database whose clock agrees with the computer's.</summary>
internal sealed class ComputerClock(TimeProvider time) : IDatabaseClock
{
    public Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default) => Task.FromResult(time.GetUtcNow().UtcDateTime);
}