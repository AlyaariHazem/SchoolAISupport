using System.Globalization;
using System.IdentityModel.Tokens.Jwt;

namespace WebApi.Security;

/// <summary>
/// Reads <c>TenantId</c> from MySchool JWT (<c>Authorization: Bearer</c>) without signature validation
/// (same trust boundary as the browser sending the token to our API).
/// </summary>
public static class ChatJwtTenantReader
{
    /// <summary>Returns null if there is no Bearer token, it cannot be read, or <c>TenantId</c> is missing/invalid.</summary>
    public static int? TryGetTenantId(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)) return null;
        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorizationHeader[prefix.Length..].Trim();
        if (string.IsNullOrEmpty(token)) return null;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token)) return null;
            var jwt = handler.ReadJwtToken(token);
            var claim = jwt.Claims.FirstOrDefault(c =>
                string.Equals(c.Type, "TenantId", StringComparison.OrdinalIgnoreCase));
            if (claim is null || string.IsNullOrWhiteSpace(claim.Value)) return null;
            return int.TryParse(claim.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0
                ? id
                : null;
        }
        catch
        {
            return null;
        }
    }
}
