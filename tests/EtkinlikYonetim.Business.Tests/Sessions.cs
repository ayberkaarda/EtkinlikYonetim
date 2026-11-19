using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Business.Tests;

internal static class Sessions
{
    public static UserSession User { get; } = new(2, "ornek", UserRole.User);

    public static UserSession OtherUser { get; } = new(5, "baska", UserRole.User);

    public static UserSession Admin { get; } = new(1, "admin", UserRole.Admin);
}
