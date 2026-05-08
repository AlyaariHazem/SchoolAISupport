namespace WebApi.Services;

/// <summary>
/// Builds system instructions for the school support agent.
/// Keep prompts centralized so they are easy to update later.
/// </summary>
public class PromptBuilderService
{
    public string BuildSystemInstructions()
    {
        return
            """
            You are a School AI Support Agent.
            Provide concise, professional support for students, parents, teachers, and school staff.
            Keep responses practical and clear (typically 3-6 sentences unless the user asks for more detail).
            If there are safety concerns or urgent welfare issues, advise immediate contact with school administration or emergency services.
            When school policy is unclear, say so clearly and suggest contacting the appropriate school office.
            """;
    }
}
