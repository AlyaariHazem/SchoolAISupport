using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace WebApi.Services;

/// <summary>
/// Stand-in <see cref="IChatClient"/> when <c>OPENAI_API_KEY</c> is missing so the host and agent registration do not fail.
/// Prefer returning HTTP 503 from API routes when <see cref="OpenAiLlmAvailability.IsConfigured"/> is false.
/// </summary>
public sealed class UnconfiguredOpenAiChatClient : IChatClient
{
    public static string AssistantReply =>
        """
        OpenAI is not configured for this server. Set the environment variable OPENAI_API_KEY to your OpenAI API key, optionally OPENAI_MODEL (default: gpt-4o-mini), then restart the application.
        """;

    public ChatClientMetadata Metadata { get; } = new("openai-unconfigured");

    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var msg = new ChatMessage(ChatRole.Assistant, [new TextContent(AssistantReply)]);
        return Task.FromResult(new ChatResponse(msg));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new(ChatRole.Assistant, [new TextContent(AssistantReply)]);
        await Task.CompletedTask;
    }
}
