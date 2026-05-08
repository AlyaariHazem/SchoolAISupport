namespace WebApi.Models;

/// <summary>
/// Who is asking — used to tailor tone and routing hints in prompts.
/// JSON: student, teacher, parent, admin (camelCase via HttpJsonOptions).
/// </summary>
public enum UserRole
{
    Student,
    Teacher,
    Parent,
    Admin
}
