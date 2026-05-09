namespace WebApi.Configuration;

/// <summary>
/// Tenant database connection for read-only school data tools (same SQL database as the MySchool tenant app).
/// </summary>
public sealed class SchoolDataOptions
{
    public const string SectionName = "SchoolData";

    /// <summary>
    /// Optional static tenant DB (single-school dev). Prefer sending <c>tenantId</c> on <c>/api/chat</c> for runtime resolution.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// MySchool **master** database (contains <c>Tenants</c>). If empty, <c>ConnectionStrings:SqlAdminConnection</c> is used.
    /// Required for per-request tenant resolution via <c>tenantId</c>.
    /// </summary>
    public string? MasterConnectionString { get; set; }

    /// <summary>
    /// When true, loads MySchool Backend <c>appsettings.json</c> + <c>appsettings.Development.json</c> (same as Development merge). Use when not running with ASPNETCORE_ENVIRONMENT=Development.
    /// </summary>
    public bool MergeBackendConfiguration { get; set; }

    /// <summary>Optional absolute path to MySchool Backend folder (contains appsettings.json). Default: ../../../MySchool/Backend from WebApi.</summary>
    public string? MySchoolBackendPath { get; set; }
}
