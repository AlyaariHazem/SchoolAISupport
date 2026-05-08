namespace WebApi.Services;

/// <summary>
/// Loads knowledge files from disk once the host starts so paths and ContentRoot are available.
/// </summary>
public sealed class SchoolKnowledgeStartupLoader : IHostedService
{
    private readonly SchoolKnowledgeService _knowledgeService;
    private readonly ILogger<SchoolKnowledgeStartupLoader> _logger;

    public SchoolKnowledgeStartupLoader(
        SchoolKnowledgeService knowledgeService,
        ILogger<SchoolKnowledgeStartupLoader> logger)
    {
        _knowledgeService = knowledgeService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _knowledgeService.LoadFromDisk();
        }
        catch (Exception ex)
        {
            // Do not fail host startup; chat flow treats empty/missing KB as "no school information".
            _logger.LogError(ex, "School knowledge base load failed; continuing with no documents.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
