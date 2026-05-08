using Microsoft.Agents.AI;
using WebApi.Models;

namespace WebApi.Services;

/// <summary>
/// Orchestrates the school support request flow:
/// - reads conversation memory
/// - adds lightweight school context
/// - applies escalation hinting
/// - calls the underlying AI agent
/// </summary>
public class AgentService
{
    private readonly AIAgent _networkSupportAgent;
    private readonly ConversationMemoryService _conversationMemoryService;
    private readonly SchoolKnowledgeService _schoolKnowledgeService;
    private readonly SupportRequestService _supportRequestService;

    public AgentService(
        IServiceProvider serviceProvider,
        ConversationMemoryService conversationMemoryService,
        SchoolKnowledgeService schoolKnowledgeService,
        SupportRequestService supportRequestService)
    {
        _networkSupportAgent = serviceProvider.GetRequiredKeyedService<AIAgent>("NetworkSupportAgent");
        _conversationMemoryService = conversationMemoryService;
        _schoolKnowledgeService = schoolKnowledgeService;
        _supportRequestService = supportRequestService;
    }

    public async Task<string> GetResponseAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // ConversationId is normalized by the HTTP layer before this call.
        var conversationId = request.ConversationId
            ?? throw new InvalidOperationException("ConversationId must be set before calling the agent.");

        var history = _conversationMemoryService.GetRecentHistory(conversationId);
        var knowledgeContext = _schoolKnowledgeService.GetKnowledgeContext(request.Message);
        var supportDecision = _supportRequestService.Evaluate(request.Message);

        var prompt = BuildInputPrompt(request, history, knowledgeContext, supportDecision);
        // AIAgent.RunAsync(string) — cancellation is checked above; underlying SDK may not accept a token on this overload.
        var result = await _networkSupportAgent.RunAsync(prompt);

        _conversationMemoryService.AddUserMessage(conversationId, request.Message);
        _conversationMemoryService.AddAssistantMessage(conversationId, result.Text);

        return result.Text;
    }

    private static string BuildInputPrompt(
        ChatRequest request,
        IReadOnlyList<ConversationTurn> history,
        string knowledgeContext,
        SupportRequestDecision supportDecision)
    {
        var historyText = history.Count == 0
            ? "No prior conversation turns."
            : string.Join(
                Environment.NewLine,
                history.Select(turn => $"{turn.Role}: {turn.Message}"));

        var escalationNote = supportDecision.ShouldEscalate
            ? $"Escalation note: {supportDecision.Reason}. Include safe next-step guidance."
            : "Escalation note: no immediate escalation trigger detected.";

        var userLabel = string.IsNullOrWhiteSpace(request.UserName)
            ? "Name not provided."
            : request.UserName.Trim();

        return
            $"""
            User context:
            Role: {request.UserRole}
            Display name: {userLabel}

            School context:
            {knowledgeContext}

            Recent conversation history:
            {historyText}

            {escalationNote}

            User message:
            {request.Message}
            """;
    }
}
