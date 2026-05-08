namespace WebApi.Services;

/// <summary>
/// Provides lightweight school-domain context snippets.
/// Replace this later with database or RAG-backed retrieval.
/// </summary>
public class SchoolKnowledgeService
{
    public string GetKnowledgeContext(string userMessage)
    {
        var message = userMessage.ToLowerInvariant();

        if (message.Contains("attendance"))
        {
            return "Attendance guidance: remind users to check attendance policy, absence reporting windows, and required documentation.";
        }

        if (message.Contains("fee") || message.Contains("payment") || message.Contains("tuition"))
        {
            return "Finance guidance: mention payment deadlines, available payment channels, and the school finance office for account-specific details.";
        }

        if (message.Contains("exam") || message.Contains("schedule") || message.Contains("timetable"))
        {
            return "Academic guidance: direct users to official timetable/calendar and advise contacting the academic office for conflicts.";
        }

        if (message.Contains("bully") || message.Contains("harass") || message.Contains("unsafe"))
        {
            return "Safety guidance: prioritize immediate student safety and escalate to school safeguarding staff or emergency contacts.";
        }

        return "General guidance: answer clearly, avoid guessing unknown policy details, and route users to the correct school office when needed.";
    }
}
