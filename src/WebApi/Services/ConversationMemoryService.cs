using System.Collections.Concurrent;

namespace WebApi.Services;

/// <summary>
/// Minimal in-memory conversation store.
/// This keeps short context across turns without introducing persistence yet.
/// </summary>
public class ConversationMemoryService
{
    private readonly ConcurrentDictionary<string, List<ConversationTurn>> _conversations = new();

    public IReadOnlyList<ConversationTurn> GetRecentHistory(string conversationId, int maxTurns = 6)
    {
        if (!_conversations.TryGetValue(conversationId, out var turns))
        {
            return [];
        }

        return turns.TakeLast(maxTurns).ToList();
    }

    public void AddUserMessage(string conversationId, string message)
    {
        AddTurn(conversationId, "user", message);
    }

    public void AddAssistantMessage(string conversationId, string message)
    {
        AddTurn(conversationId, "assistant", message);
    }

    private void AddTurn(string conversationId, string role, string message)
    {
        var turns = _conversations.GetOrAdd(conversationId, _ => []);
        lock (turns)
        {
            turns.Add(new ConversationTurn(role, message, DateTimeOffset.UtcNow));

            // Keep memory bounded so the service stays lightweight.
            const int maxStoredTurns = 30;
            if (turns.Count > maxStoredTurns)
            {
                turns.RemoveRange(0, turns.Count - maxStoredTurns);
            }
        }
    }
}

public record ConversationTurn(string Role, string Message, DateTimeOffset TimestampUtc);
