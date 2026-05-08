using Microsoft.Extensions.AI;

namespace WebApi.Services;

/// <summary>
/// Produces a concise summary of school-provided text using the same chat model as the agent.
/// </summary>
public class SchoolDocumentSummarizationService
{
    public const int MaxInputLength = 48_000;

    private readonly IChatClient _chatClient;

    public SchoolDocumentSummarizationService(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> SummarizeAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is required.");
        }

        var trimmed = text.Trim();
        if (trimmed.Length > MaxInputLength)
        {
            throw new ArgumentException($"Text must be at most {MaxInputLength} characters.");
        }

        var system =
            """
            You summarize school-related documents for staff, teachers, or parents.
            Use clear bullet points or short paragraphs. Do not invent policies or facts not present in the text.
            If the text is not in Arabic, still summarize in the same language as the source unless the user asks otherwise.
            """;

        var user =
            $"""
            Summarize the following school text. Highlight key dates, obligations, and contacts if mentioned.

            ---
            {trimmed}
            ---
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, system),
            new(ChatRole.User, user)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return GetAssistantText(response);
    }

    private static string GetAssistantText(ChatResponse response)
    {
        if (!string.IsNullOrEmpty(response.Text))
        {
            return response.Text;
        }

        var last = response.Messages?.LastOrDefault(m => m.Role == ChatRole.Assistant);
        if (last?.Contents is { Count: > 0 })
        {
            foreach (var c in last.Contents)
            {
                if (c is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                {
                    return tc.Text;
                }
            }
        }

        return string.Empty;
    }
}
