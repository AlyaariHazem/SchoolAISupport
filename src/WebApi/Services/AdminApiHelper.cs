using WebApi.Models;

namespace WebApi.Services;

/// <summary>
/// Minimal admin check until real auth is wired (e.g. Azure AD).
/// </summary>
public static class AdminApiHelper
{
    public const string UserRoleHeaderName = "X-User-Role";

    public static bool TryGetCallerRole(HttpRequest request, out UserRole role)
    {
        role = default;
        if (!request.Headers.TryGetValue(UserRoleHeaderName, out var values))
        {
            return false;
        }

        var raw = values.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return Enum.TryParse(raw, ignoreCase: true, out role);
    }

    public static bool IsAdmin(HttpRequest request) =>
        TryGetCallerRole(request, out var role) && role == UserRole.Admin;
}
