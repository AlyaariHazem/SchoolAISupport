namespace WebApi.Endpoints;

/// <summary>
/// Registers all School AI Support Agent HTTP APIs (grouped under Swagger tags).
/// </summary>
public static class SchoolAgentApiExtensions
{
    public static WebApplication MapSchoolAgentApi(this WebApplication app)
    {
        app.MapChatEndpoints();
        app.MapSupportEndpoints();
        app.MapAdminEndpoints();
        app.MapToolsEndpoints();
        return app;
    }
}
