namespace WebApi.Services;

/// <summary>
/// Whether the app has a valid OpenAI API configuration (key present at startup).
/// </summary>
public sealed class OpenAiLlmAvailability
{
    public bool IsConfigured { get; init; }

    /// <summary>
    /// Human-readable message when <see cref="IsConfigured"/> is false.
    /// </summary>
    public string ConfigurationHint { get; init; } = "";
}
