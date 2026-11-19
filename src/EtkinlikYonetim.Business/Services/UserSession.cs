using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Business.Services;

/// <summary>
/// The signed-in account. Only <see cref="AuthService"/> creates sessions (the constructor is internal),
/// and every service takes one and checks the role and the ownership of the data against it, so the
/// user interface cannot widen access by passing other ids.
/// </summary>
public sealed class UserSession
{
    internal UserSession(int userId, string userName, UserRole role, int sessionVersion = 0)
    {
        UserId = userId;
        UserName = userName;
        Role = role;
        SessionVersion = sessionVersion;
    }

    public int UserId { get; }

    public string UserName { get; }

    public UserRole Role { get; }

    public bool IsAdmin => Role == UserRole.Admin;

    /// <summary>The account's session version at sign-in; any later change of the account's access ends the session.</summary>
    internal int SessionVersion { get; }
}
