using WebApi.Models;

namespace WebApi.Services;

public static class SupportTicketRequestValidator
{
    public const int MaxIssueLength = 8_000;

    public static IReadOnlyDictionary<string, string[]> Validate(CreateSupportTicketRequest request)
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

        if (string.IsNullOrWhiteSpace(request.Issue))
        {
            Add(nameof(request.Issue), "Issue is required.");
        }
        else if (request.Issue.Length > MaxIssueLength)
        {
            Add(nameof(request.Issue), $"Issue must be at most {MaxIssueLength} characters.");
        }

        if (!string.IsNullOrWhiteSpace(request.UserName) && request.UserName.Length > 200)
        {
            Add(nameof(request.UserName), "UserName must be at most 200 characters.");
        }

        return errors.ToDictionary(static kv => kv.Key, static kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsValid(IReadOnlyDictionary<string, string[]> errors) => errors.Count == 0;
}
