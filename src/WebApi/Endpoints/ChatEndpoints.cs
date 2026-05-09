using WebApi.Infrastructure;
using WebApi.Models;
using WebApi.Security;
using WebApi.Services;
using WebApi.Services.Data;

namespace WebApi.Endpoints;

/// <summary>
/// Role-aware school assistant chat.
/// </summary>
public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        app.MapPost("/api/chat", async (
                ChatRequest request,
                HttpContext httpContext,
                AgentService agentService,
                ITenantConnectionStringResolver tenantResolver,
                OpenAiLlmAvailability openAi,
                IHostEnvironment hostEnvironment,
                CancellationToken cancellationToken,
                ILoggerFactory loggerFactory) =>
            {
                var log = loggerFactory.CreateLogger("SchoolChat");

                if (!openAi.IsConfigured)
                {
                    return Results.Json(
                        new { error = openAi.ConfigurationHint },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                var trimmedRequest = request with
                {
                    Message = request.Message.Trim(),
                    ConversationId = string.IsNullOrWhiteSpace(request.ConversationId) ? null : request.ConversationId.Trim(),
                    UserName = string.IsNullOrWhiteSpace(request.UserName) ? null : request.UserName.Trim(),
                };

                var validationErrors = ChatRequestValidator.Validate(trimmedRequest);
                if (!ChatRequestValidator.IsValid(validationErrors))
                {
                    return Results.ValidationProblem(validationErrors);
                }

                var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
                var hasBearer = !string.IsNullOrEmpty(authHeader)
                    && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

                if (hasBearer)
                {
                    var tid = ChatJwtTenantReader.TryGetTenantId(authHeader);
                    if (tid is null)
                    {
                        return Results.Json(
                            new
                            {
                                error =
                                    "Authorization Bearer token is present but TenantId could not be read. Use a MySchool access JWT that includes the TenantId claim.",
                            },
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var resolved = await tenantResolver.ResolveAsync(tid.Value, cancellationToken);
                    if (!resolved.Ok || string.IsNullOrWhiteSpace(resolved.ConnectionString))
                    {
                        var summary =
                            $"Could not load school database for tenant {tid.Value}. Ensure SqlAdminConnection targets the MySchool **master** database (dbo.Tenants), the tenant row exists, and SQL is reachable.";
                        return Results.Json(
                            new
                            {
                                error = summary,
                                detail = hostEnvironment.IsDevelopment() ? resolved.FailureReason : null,
                            },
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    httpContext.Items[SchoolDataConnection.HttpContextItemKey] = resolved.ConnectionString;
                }

                var conversationId = string.IsNullOrWhiteSpace(trimmedRequest.ConversationId)
                    ? Guid.NewGuid().ToString("N")
                    : trimmedRequest.ConversationId;

                var normalizedRequest = trimmedRequest with { ConversationId = conversationId };

                try
                {
                    var responseText = await agentService.GetResponseAsync(normalizedRequest, cancellationToken);
                    return Results.Ok(new SchoolChatResponse(conversationId, responseText));
                }
                catch (OperationCanceledException)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status499ClientClosedRequest,
                        title: "Request cancelled",
                        detail: "The client closed the connection before the assistant finished.");
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Assistant failed for conversation {ConversationId}", conversationId);
                    return Results.Problem(
                        statusCode: StatusCodes.Status502BadGateway,
                        title: "Assistant unavailable",
                        detail: "The assistant could not complete your request. Please try again later.");
                }
            })
            .WithName("SchoolChat")
            .WithTags("Chat")
            .WithSummary("School AI assistant conversation")
            .WithDescription(
                "Arabic-first assistant. With Authorization: Bearer (MySchool JWT), TenantId is read from the token and the tenant DB is resolved via the master Tenants table. Without Bearer, only static SchoolData:ConnectionString applies. Returns conversationId for follow-up turns.")
            .Produces<SchoolChatResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}
