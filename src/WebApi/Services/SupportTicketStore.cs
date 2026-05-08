using System.Collections.Concurrent;
using WebApi.Models;

namespace WebApi.Services;

/// <summary>
/// In-memory store for support tickets created when issues need human follow-up.
/// </summary>
public class SupportTicketStore
{
    private readonly ConcurrentDictionary<string, SupportTicket> _tickets = new();

    public SupportTicket Create(CreateSupportTicketRequest request)
    {
        var id = Guid.NewGuid().ToString("N");
        var ticket = new SupportTicket(
            id,
            string.IsNullOrWhiteSpace(request.UserName) ? null : request.UserName.Trim(),
            request.UserRole,
            request.Issue.Trim(),
            request.Category,
            request.Priority,
            DateTimeOffset.UtcNow,
            SupportTicketStatus.Open);

        _tickets[id] = ticket;
        return ticket;
    }

    public IReadOnlyList<SupportTicket> ListAllOrderedByCreatedDesc()
    {
        return _tickets.Values
            .OrderByDescending(t => t.CreatedAt)
            .ToList();
    }

    public SupportTicket? GetById(string id) =>
        _tickets.TryGetValue(id, out var t) ? t : null;
}
