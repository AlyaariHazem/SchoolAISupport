using WebApi.Models;
using WebApi.Services;

namespace WebApi.Endpoints;

/// <summary>
/// Support ticket creation and lookup (in-memory store).
/// </summary>
public static class SupportEndpoints
{
    public static void MapSupportEndpoints(this WebApplication app)
    {
        app.MapPost("/api/support/requests", (CreateSupportTicketRequest body, SupportTicketStore store) =>
            {
                var errors = SupportTicketRequestValidator.Validate(body);
                if (!SupportTicketRequestValidator.IsValid(errors))
                {
                    return Results.ValidationProblem(errors);
                }

                var ticket = store.Create(body);
                return Results.Created($"/api/support/requests/{ticket.Id}", ticket);
            })
            .WithName("CreateSupportTicket")
            .WithTags("Support")
            .WithSummary("Create support ticket")
            .WithDescription("Escalate when the assistant or user cannot resolve an issue. Categories: academic, attendance, technical, admin, other.")
            .Produces<SupportTicket>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        app.MapGet("/api/support/requests/{id}", (string id, SupportTicketStore store) =>
            {
                var ticket = store.GetById(id);
                return ticket is null ? Results.NotFound() : Results.Ok(ticket);
            })
            .WithName("GetSupportTicketById")
            .WithTags("Support")
            .WithSummary("Get support ticket by id")
            .Produces<SupportTicket>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
