namespace WebApi.Services.Data;

/// <summary>
/// Looks up a tenant school database connection string from the MySchool **master** database (<c>Tenants</c> table).
/// </summary>
public interface ITenantConnectionStringResolver
{
    Task<TenantConnectionResolveResult> ResolveAsync(int tenantId, CancellationToken cancellationToken = default);
}
