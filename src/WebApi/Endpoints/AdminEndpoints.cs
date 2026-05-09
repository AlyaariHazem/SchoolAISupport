using WebApi.Models;
using WebApi.Services;

namespace WebApi.Endpoints;

/// <summary>
/// Admin-only operations (header-based gate until real auth).
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/support-requests", (HttpRequest http, SupportTicketStore store) =>
            {
                if (!AdminApiHelper.IsAdmin(http))
                {
                    return Results.Json(
                        new
                        {
                            error = "Admin access required.",
                            hint = $"Send header {AdminApiHelper.UserRoleHeaderName}: admin"
                        },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                return Results.Ok(store.ListAllOrderedByCreatedDesc());
            })
            .WithName("ListSupportTicketsAdmin")
            .WithTags("Admin")
            .WithSummary("List all support tickets")
            .WithDescription($"Requires header `{AdminApiHelper.UserRoleHeaderName}: admin`.")
            .Produces<IReadOnlyList<SupportTicket>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);
    }
}
