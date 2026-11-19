using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Business.Services;

/// <summary>
/// The signed-in account. Only <see cref="AuthService"/> creates sessions (the constructor is internal),
/// and every service takes one and checks the role and the ownership of the data against it, so the
/// user interface cannot widen access by passing other ids.
/// </summary>
public sealed class UserSession
{
    internal UserSession(int userId, string userName, UserRole role)
    {
        UserId = userId;
        UserName = userName;
        Role = role;
    }

    public int UserId { get; }

    public string UserName { get; }

    public UserRole Role { get; }

    public bool IsAdmin => Role == UserRole.Admin;
}
