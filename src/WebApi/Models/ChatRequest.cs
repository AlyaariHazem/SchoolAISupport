namespace WebApi.Models;

/// <summary>
/// Incoming body for POST /api/chat.
/// </summary>
public record ChatRequest(
    string Message,
    UserRole UserRole,
    string? ConversationId = null,
    string? UserName = null);
