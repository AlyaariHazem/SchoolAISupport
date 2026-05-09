namespace WebApi.Configuration;

/// <summary>
/// Same section shape as MySchool Backend (<c>OpenAI</c>) so you can reuse env vars or copy settings.
/// Prefer User Secrets or environment variables — never commit API keys.
/// </summary>
public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    /// <summary>API key (e.g. sk-...).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Chat model id (e.g. gpt-4o-mini).</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Optional override (official API default is used when empty).</summary>
    public string BaseUrl { get; set; } = "";
}
