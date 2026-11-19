using EtkinlikYonetim.Data.Repositories;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// A deterministic interleaving point: a sign-in that has verified the password stops right before
/// it records the success, until the test releases it.
/// </summary>
internal sealed class SuccessWriteGate
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when a sign-in waits at the gate.</summary>
    public Task Reached => _reached.Task;

    public void Release() => _released.TrySetResult();

    internal async Task PassAsync(CancellationToken cancellationToken)
    {
        _reached.TrySetResult();
        await _released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal static class SuccessWriteGateExtensions
{
    /// <summary>Replaces the user repository with one that waits at <paramref name="gate"/>.</summary>
    public static IServiceCollection HoldSuccessfulSignInWrites(this IServiceCollection services, SuccessWriteGate gate)
    {
        var registration = services.Single(d => d.ServiceType == typeof(IUserRepository));
        services.Remove(registration);
        services.AddSingleton<IUserRepository>(provider =>
            new GatedUserRepository(ActivatorUtilities.CreateInstance<UserRepository>(provider), gate));
        return services;
    }

    private sealed class GatedUserRepository(IUserRepository inner, SuccessWriteGate gate) : IUserRepository
    {
        public Task<AccountCredentials?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default) =>
            inner.FindByUserNameAsync(userName, cancellationToken);

        public Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken = default) =>
            inner.UserNameExistsAsync(userName, cancellationToken);

        public Task<int> AddAsync(User user, CancellationToken cancellationToken = default) =>
            inner.AddAsync(user, cancellationToken);

        public Task RecordFailedLoginAsync(int userId, int maxAttempts, DateTime lockoutEnd, CancellationToken cancellationToken = default) =>
            inner.RecordFailedLoginAsync(userId, maxAttempts, lockoutEnd, cancellationToken);

        public async Task<bool> RecordSuccessfulLoginAsync(
            AccountCredentials verified,
            DateTime now,
            string? newPasswordHash,
            CancellationToken cancellationToken = default)
        {
            await gate.PassAsync(cancellationToken);
            return await inner.RecordSuccessfulLoginAsync(verified, now, newPasswordHash, cancellationToken);
        }
    }
}
