using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using WebApi.Configuration;

namespace WebApi.Services.Data;

/// <summary>
/// Reads <c>SELECT ConnectionString FROM Tenants WHERE TenantId = @id</c> using the admin/master connection string.
/// </summary>
public sealed class SqlMasterTenantConnectionResolver(
    IOptions<SchoolDataOptions> schoolOptions,
    IConfiguration configuration,
    ILogger<SqlMasterTenantConnectionResolver> logger) : ITenantConnectionStringResolver
{
    public async Task<TenantConnectionResolveResult> ResolveAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0)
            return TenantConnectionResolveResult.Fail("Invalid tenant id.");

        // Treat whitespace/empty SchoolData:MasterConnectionString as unset so merged ConnectionStrings win.
        var fromOptions = schoolOptions.Value.MasterConnectionString?.Trim();
        var master = !string.IsNullOrWhiteSpace(fromOptions)
            ? fromOptions
            : configuration.GetConnectionString("SqlAdminConnection")?.Trim()
                ?? configuration.GetConnectionString("DefaultConnection")?.Trim();

        if (string.IsNullOrEmpty(master))
        {
            var hint =
                "No master SQL connection is configured. Set ConnectionStrings:SqlAdminConnection (or SchoolData:MasterConnectionString) to the MySchool **admin** database that contains table Tenants. " +
                "If minimal-agent runs outside Development, set SchoolData:MergeBackendConfiguration=true or copy SqlAdminConnection from MySchool Backend appsettings, or set env ConnectionStrings__SqlAdminConnection.";
            logger.LogWarning("Tenant {TenantId}: {Hint}", tenantId, hint);
            return TenantConnectionResolveResult.Fail(hint);
        }

        try
        {
            await using var conn = new SqlConnection(master);
            await conn.OpenAsync(cancellationToken);
            const string sql = "SELECT ConnectionString FROM dbo.Tenants WHERE TenantId = @id;";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = tenantId;
            var result = await cmd.ExecuteScalarAsync(cancellationToken);

            if (result is null || result is DBNull)
            {
                var msg =
                    $"No row in dbo.Tenants for TenantId={tenantId}, or ConnectionString is NULL. Verify in SQL: SELECT TenantId, LEN(ISNULL(ConnectionString,'')) FROM dbo.Tenants WHERE TenantId={tenantId}.";
                logger.LogWarning(msg);
                return TenantConnectionResolveResult.Fail(msg);
            }

            var cs = Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(cs))
            {
                var msg = $"Tenants.ConnectionString is empty for TenantId={tenantId}.";
                logger.LogWarning(msg);
                return TenantConnectionResolveResult.Fail(msg);
            }

            return TenantConnectionResolveResult.Success(cs);
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "SQL error resolving tenant {TenantId} from master.", tenantId);
            return TenantConnectionResolveResult.Fail(
                $"SQL error connecting or querying master database: {ex.Message} (Error {ex.Number}). " +
                "Confirm SqlAdminConnection points to the database that has AspNetUsers + Tenants (not a tenant-only database).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load connection string for tenant {TenantId} from master database.", tenantId);
            return TenantConnectionResolveResult.Fail(ex.Message);
        }
    }
}
