using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Domain.Repositories;

/// <summary>
/// The account a write acts for, as its session was issued: the account id, the role and the session
/// version. Every write checks it again against the stored account inside its own transaction, after it
/// has taken its locks, so an account disabled, demoted or changed and changed back while the write
/// waited changes nothing.
/// </summary>
public readonly record struct Actor(int UserId, UserRole Role, int SessionVersion);

/// <summary>
/// The account a write acts for no longer exists, is disabled, has another role or another session
/// version than its session; nothing was written.
/// </summary>
public sealed class ActorRevokedException : Exception
{
    public ActorRevokedException()
        : base("The acting account's access changed; nothing was written.")
    {
    }

    public ActorRevokedException(string message)
        : base(message)
    {
    }

    public ActorRevokedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
