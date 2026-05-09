namespace WebApi.Services.Data;

/// <summary>Outcome of resolving <see cref="Tenants"/> connection string from the master database.</summary>
public sealed record TenantConnectionResolveResult(bool Ok, string? ConnectionString, string? FailureReason)
{
    public static TenantConnectionResolveResult Success(string connectionString) =>
        new(true, connectionString, null);

    public static TenantConnectionResolveResult Fail(string reason) =>
        new(false, null, reason);
}
