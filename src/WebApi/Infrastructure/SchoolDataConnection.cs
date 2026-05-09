namespace WebApi.Infrastructure;

/// <summary>
/// Per-request tenant database connection for school data tools (set from <c>POST /api/chat</c> when <c>tenantId</c> is sent).
/// </summary>
public static class SchoolDataConnection
{
    public const string HttpContextItemKey = "MinimalAgent.SchoolTenantConnectionString";
}
