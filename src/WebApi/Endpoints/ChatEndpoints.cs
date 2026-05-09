using WebApi.Models;
using WebApi.Services;

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
                AgentService agentService,
                OpenAiLlmAvailability openAi,
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
                    UserName = string.IsNullOrWhiteSpace(request.UserName) ? null : request.UserName.Trim()
                };

                var validationErrors = ChatRequestValidator.Validate(trimmedRequest);
                if (!ChatRequestValidator.IsValid(validationErrors))
                {
                    return Results.ValidationProblem(validationErrors);
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
                "Arabic-first assistant with knowledge-base grounding. Requires OPENAI_API_KEY. Returns conversationId for follow-up turns.")
            .Produces<SchoolChatResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}
