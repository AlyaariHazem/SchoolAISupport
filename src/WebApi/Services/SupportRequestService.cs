namespace WebApi.Services;

/// <summary>
/// Evaluates whether the user message should be treated as an escalation candidate.
/// This is intentionally simple and rule-based for now.
/// </summary>
public class SupportRequestService
{
    public SupportRequestDecision Evaluate(string userMessage)
    {
        var message = userMessage.ToLowerInvariant();
        string[] escalationKeywords = ["bully", "harass", "threat", "unsafe", "urgent", "emergency"];

        foreach (var keyword in escalationKeywords)
        {
            if (message.Contains(keyword))
            {
                return new SupportRequestDecision(
                    ShouldEscalate: true,
                    Reason: $"Detected escalation keyword: {keyword}");
            }
        }

        return new SupportRequestDecision(false, null);
    }
}

public record SupportRequestDecision(bool ShouldEscalate, string? Reason);
