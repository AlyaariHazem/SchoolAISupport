namespace WebApi.Models;

/// <summary>Support ticket category for routing to the right school team.</summary>
public enum SupportTicketCategory
{
    Academic,
    Attendance,
    Technical,
    Admin,
    Other
}

/// <summary>How urgent follow-up is.</summary>
public enum SupportTicketPriority
{
    Low,
    Medium,
    High
}

/// <summary>Lifecycle state; new tickets start as <see cref="Open"/>.</summary>
public enum SupportTicketStatus
{
    Open
}

/// <summary>Persisted support request (in-memory store for now).</summary>
public record SupportTicket(
    string Id,
    string? UserName,
    UserRole UserRole,
    string Issue,
    SupportTicketCategory Category,
    SupportTicketPriority Priority,
    DateTimeOffset CreatedAt,
    SupportTicketStatus Status);

/// <summary>API body to create a ticket (e.g. when the assistant or client escalates).</summary>
public record CreateSupportTicketRequest(
    string Issue,
    SupportTicketCategory Category,
    SupportTicketPriority Priority,
    UserRole UserRole,
    string? UserName = null);

/// <summary>Request to generate quiz items from a topic and/or source text.</summary>
public record GenerateQuizRequest(
    string? Topic = null,
    string? SourceText = null,
    int QuestionCount = 5);

/// <summary>One multiple-choice question in the quiz output.</summary>
public record QuizQuestionItem(
    string Question,
    IReadOnlyList<string> Options,
    int CorrectOptionIndex);

public record GenerateQuizResponse(IReadOnlyList<QuizQuestionItem> Questions);

/// <summary>Request to summarize school-provided text (not a substitute for official documents).</summary>
public record SummarizeSchoolDocumentRequest(string Text);

public record SummarizeSchoolDocumentResponse(string Summary);
