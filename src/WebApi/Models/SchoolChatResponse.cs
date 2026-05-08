namespace WebApi.Models;

/// <summary>
/// Successful /api/chat payload. Named to avoid clashing with Microsoft.Extensions.AI.ChatResponse.
/// </summary>
public record SchoolChatResponse(string ConversationId, string Response);
