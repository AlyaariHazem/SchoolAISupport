using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using WebApi.Models;

namespace WebApi.Services;

/// <summary>
/// Simple file-based school knowledge: load .txt/.md at startup, chunk, then keyword-score for retrieval.
/// </summary>
public class SchoolKnowledgeService
{
    public const string KnowledgeFolderName = "KnowledgeBase";
    private const int TargetChunkChars = 900;
    private const int MaxChunkChars = 1400;
    private const int DefaultTopK = 4;
    private const int MinScoreToConsiderRelevant = 1;

    private readonly IHostEnvironment _environment;
    private readonly ILogger<SchoolKnowledgeService> _logger;
    private IReadOnlyList<KnowledgeDocumentChunk> _chunks = Array.Empty<KnowledgeDocumentChunk>();

    public SchoolKnowledgeService(IHostEnvironment environment, ILogger<SchoolKnowledgeService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Called from <see cref="SchoolKnowledgeStartupLoader"/> after the app is built.
    /// </summary>
    public void LoadFromDisk()
    {
        var root = Path.Combine(_environment.ContentRootPath, KnowledgeFolderName);
        if (!Directory.Exists(root))
        {
            _logger.LogWarning(
                "Knowledge folder not found at {Path}. No school documents will be available until it exists.",
                root);
            _chunks = Array.Empty<KnowledgeDocumentChunk>();
            return;
        }

        var files = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => string.Equals(Path.GetExtension(p), ".txt", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Path.GetExtension(p), ".md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allChunks = new List<KnowledgeDocumentChunk>();
        foreach (var fullPath in files)
        {
            try
            {
                var relative = Path.GetRelativePath(root, fullPath);
                var text = File.ReadAllText(fullPath, Encoding.UTF8);
                var fileName = Path.GetFileName(fullPath);
                var pieces = ChunkText(text);
                for (var i = 0; i < pieces.Count; i++)
                {
                    allChunks.Add(new KnowledgeDocumentChunk(relative, fileName, i, pieces[i]));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping knowledge file {Path} due to read/parse error.", fullPath);
            }
        }

        _chunks = allChunks;
        _logger.LogInformation(
            "Loaded school knowledge: {FileCount} files, {ChunkCount} chunks from {Root}.",
            files.Count,
            _chunks.Count,
            root);
    }

    /// <summary>
    /// Returns the best-matching chunks for the user query using simple token overlap scoring.
    /// </summary>
    public KnowledgeSearchResult Search(string userMessage, int topK = DefaultTopK)
    {
        if (_chunks.Count == 0)
        {
            return new KnowledgeSearchResult([], false);
        }

        var queryTokens = Tokenize(userMessage);
        if (queryTokens.Count == 0)
        {
            return new KnowledgeSearchResult([], false);
        }

        var scored = new List<(KnowledgeDocumentChunk Chunk, int Score)>(_chunks.Count);
        foreach (var chunk in _chunks)
        {
            var score = ScoreChunk(chunk.Text, queryTokens);
            if (score > 0)
            {
                scored.Add((chunk, score));
            }
        }

        if (scored.Count == 0)
        {
            return new KnowledgeSearchResult([], false);
        }

        var top = scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.SourceRelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Chunk.ChunkIndex)
            .Take(topK)
            .ToList();

        var bestScore = top[0].Score;
        var relevant = bestScore >= MinScoreToConsiderRelevant;
        return new KnowledgeSearchResult(top, relevant);
    }

    /// <summary>
    /// Builds the text block injected into the agent prompt (retrieved excerpts or explicit “no data” instruction).
    /// </summary>
    public string BuildPromptContext(string userMessage, int topK = DefaultTopK)
    {
        var result = Search(userMessage, topK);
        if (!result.HasRelevantContent || result.TopChunks.Count == 0)
        {
            return
                """
                School knowledge base retrieval:
                No relevant excerpts were found in the loaded school documents for this question (or the knowledge folder is empty).

                You MUST tell the user clearly—in their language—that you do not have enough verified school-document information to answer accurately, and they should contact the appropriate school office or check the official portal/handbook. Do not invent policies, dates, fees, or schedules.
                """;
        }

        var sb = new StringBuilder();
        sb.AppendLine("School knowledge base retrieval (use ONLY these excerpts for school-specific facts; do not add facts not present here):");
        sb.AppendLine();
        for (var i = 0; i < result.TopChunks.Count; i++)
        {
            var (chunk, score) = result.TopChunks[i];
            sb.AppendLine($"--- Excerpt {i + 1} (score {score}, source: {chunk.SourceRelativePath}, chunk {chunk.ChunkIndex}) ---");
            sb.AppendLine(chunk.Text.Trim());
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static List<string> ChunkText(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        // Split on blank lines first (typical paragraphs / markdown sections).
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chunks = new List<string>();
        foreach (var para in paragraphs)
        {
            if (para.Length <= MaxChunkChars)
            {
                if (para.Length >= 40 || chunks.Count == 0)
                {
                    chunks.Add(para);
                }
                else
                {
                    chunks[^1] = chunks[^1] + "\n\n" + para;
                }

                continue;
            }

            // Long paragraph: split into windows near TargetChunkChars on newline or space.
            chunks.AddRange(SplitLongParagraph(para));
        }

        return MergeTinyChunks(chunks, minChars: 80);
    }

    private static IEnumerable<string> SplitLongParagraph(string para)
    {
        var start = 0;
        while (start < para.Length)
        {
            var remaining = para.Length - start;
            if (remaining <= MaxChunkChars)
            {
                yield return para[start..].Trim();
                yield break;
            }

            var end = Math.Min(start + TargetChunkChars, para.Length);
            var window = para[start..end];
            var breakAt = window.LastIndexOf('\n');
            if (breakAt < TargetChunkChars / 2)
            {
                breakAt = window.LastIndexOf(' ');
            }

            if (breakAt < TargetChunkChars / 2)
            {
                breakAt = window.Length;
            }

            var take = Math.Max(1, breakAt);
            var piece = para.Substring(start, take).Trim();
            if (piece.Length > 0)
            {
                yield return piece;
            }

            start += take;
            while (start < para.Length && char.IsWhiteSpace(para[start]))
            {
                start++;
            }
        }
    }

    private static List<string> MergeTinyChunks(List<string> chunks, int minChars)
    {
        if (chunks.Count <= 1)
        {
            return chunks;
        }

        var merged = new List<string> { chunks[0] };
        for (var i = 1; i < chunks.Count; i++)
        {
            if (merged[^1].Length < minChars)
            {
                merged[^1] = merged[^1] + "\n\n" + chunks[i];
            }
            else
            {
                merged.Add(chunks[i]);
            }
        }

        return merged;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var matches = Regex.Matches(text, @"[\p{L}\p{Nd}]+", RegexOptions.CultureInvariant);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in matches)
        {
            if (m.Value.Length >= 2)
            {
                set.Add(m.Value);
            }
        }

        return set;
    }

    private static int ScoreChunk(string chunkText, HashSet<string> queryTokens)
    {
        var chunkLower = chunkText.ToLowerInvariant();
        var score = 0;
        foreach (var token in queryTokens)
        {
            var t = token.ToLowerInvariant();
            var idx = 0;
            var hits = 0;
            while ((idx = chunkLower.IndexOf(t, idx, StringComparison.Ordinal)) >= 0)
            {
                hits++;
                idx += t.Length;
                if (hits >= 3)
                {
                    break;
                }
            }

            score += hits;
        }

        return score;
    }
}

/// <summary>
/// Result of keyword search over loaded chunks.
/// </summary>
public record KnowledgeSearchResult(
    IReadOnlyList<(KnowledgeDocumentChunk Chunk, int Score)> TopChunks,
    bool HasRelevantContent);
