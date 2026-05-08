using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using WebApi.Models;

namespace WebApi.Services;

/// <summary>
/// Uses the chat model to produce quiz questions from a topic or pasted school text.
/// </summary>
public class QuizGenerationService
{
    private const int MaxSourceTextLength = 24_000;
    private const int MaxTopicLength = 500;
    private const int MinQuestions = 1;
    private const int MaxQuestions = 20;

    private readonly IChatClient _chatClient;
    private readonly ILogger<QuizGenerationService> _logger;

    public QuizGenerationService(IChatClient chatClient, ILogger<QuizGenerationService> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<GenerateQuizResponse> GenerateAsync(GenerateQuizRequest request, CancellationToken cancellationToken = default)
    {
        var count = Math.Clamp(request.QuestionCount, MinQuestions, MaxQuestions);
        var topic = string.IsNullOrWhiteSpace(request.Topic) ? null : request.Topic.Trim();
        var text = string.IsNullOrWhiteSpace(request.SourceText) ? null : request.SourceText.Trim();

        if (topic is null && text is null)
        {
            throw new ArgumentException("Provide at least Topic or SourceText.");
        }

        if (topic is not null && topic.Length > MaxTopicLength)
        {
            throw new ArgumentException($"Topic must be at most {MaxTopicLength} characters.");
        }

        if (text is not null && text.Length > MaxSourceTextLength)
        {
            throw new ArgumentException($"SourceText must be at most {MaxSourceTextLength} characters.");
        }

        var userBlock = new System.Text.StringBuilder();
        if (topic is not null)
        {
            userBlock.AppendLine("Topic: ");
            userBlock.AppendLine(topic);
            userBlock.AppendLine();
        }

        if (text is not null)
        {
            userBlock.AppendLine("Source text to base questions on:");
            userBlock.AppendLine(text);
            userBlock.AppendLine();
        }

        userBlock.AppendLine($"Generate exactly {count} multiple-choice questions.");
        userBlock.AppendLine("Each question must have exactly 4 options.");
        userBlock.AppendLine("Output ONLY a JSON array (no markdown fences), each element:");
        userBlock.AppendLine("{\"question\":\"...\",\"options\":[\"A\",\"B\",\"C\",\"D\"],\"correctOptionIndex\":0}");
        userBlock.AppendLine("correctOptionIndex is 0-3.");

        var system =
            """
            You create fair, clear quiz questions for school contexts. Use only the provided topic and/or source text.
            If the source is thin, still produce plausible distractors. Respond with valid JSON only.
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, system),
            new(ChatRole.User, userBlock.ToString())
        };

        var raw = await CompleteChatAsync(messages, cancellationToken);
        var json = ExtractJsonArray(raw);
        try
        {
            var dtos = JsonSerializer.Deserialize<List<QuizQuestionDto>>(json, JsonOptions)
                       ?? [];
            var items = new List<QuizQuestionItem>();
            foreach (var dto in dtos.Take(count))
            {
                if (dto.Options is null || dto.Options.Count != 4)
                {
                    continue;
                }

                var idx = Math.Clamp(dto.CorrectOptionIndex, 0, 3);
                items.Add(new QuizQuestionItem(dto.Question.Trim(), dto.Options.Select(o => o.Trim()).ToList(), idx));
            }

            if (items.Count == 0)
            {
                throw new InvalidOperationException("Model returned no usable quiz questions.");
            }

            return new GenerateQuizResponse(items);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Quiz JSON parse failed. Raw (truncated): {Raw}", Truncate(raw, 500));
            throw;
        }
    }

    private async Task<string> CompleteChatAsync(IList<ChatMessage> messages, CancellationToken cancellationToken)
    {
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

    private static string ExtractJsonArray(string raw)
    {
        var t = raw.Trim();
        var start = t.IndexOf('[');
        var end = t.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return t[start..(end + 1)];
        }

        return t;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed class QuizQuestionDto
    {
        [JsonPropertyName("question")]
        public string Question { get; set; } = "";

        [JsonPropertyName("options")]
        public List<string>? Options { get; set; }

        [JsonPropertyName("correctOptionIndex")]
        public int CorrectOptionIndex { get; set; }
    }
}
