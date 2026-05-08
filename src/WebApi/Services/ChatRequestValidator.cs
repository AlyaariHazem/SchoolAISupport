using WebApi.Models;

namespace WebApi.Services;

/// <summary>
/// Validates /api/chat input. Keeps rules in one place for easy tuning.
/// </summary>
public static class ChatRequestValidator
{
    public const int MaxMessageLength = 8_000;
    public const int MaxUserNameLength = 200;
    public const int MaxConversationIdLength = 128;

    /// <summary>
    /// Returns human-readable error messages suitable for ValidationProblem details.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> Validate(ChatRequest request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list))
            {
                list = [];
                errors[key] = list;
            }

            list.Add(message);
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            Add(nameof(request.Message), "Message is required.");
        }
        else if (request.Message.Length > MaxMessageLength)
        {
            Add(nameof(request.Message), $"Message must be at most {MaxMessageLength} characters.");
        }

        if (!string.IsNullOrWhiteSpace(request.UserName) && request.UserName.Length > MaxUserNameLength)
        {
            Add(nameof(request.UserName), $"User name must be at most {MaxUserNameLength} characters.");
        }

        if (!string.IsNullOrWhiteSpace(request.ConversationId) && request.ConversationId.Length > MaxConversationIdLength)
        {
            Add(nameof(request.ConversationId), $"Conversation id must be at most {MaxConversationIdLength} characters.");
        }

        return errors.ToDictionary(
            static kv => kv.Key,
            static kv => kv.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsValid(IReadOnlyDictionary<string, string[]> errors) => errors.Count == 0;
}
