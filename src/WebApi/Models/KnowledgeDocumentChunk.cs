namespace WebApi.Models;

/// <summary>
/// A segment of text from a school knowledge file, used for keyword retrieval and prompt injection.
/// </summary>
public record KnowledgeDocumentChunk(
    string SourceRelativePath,
    string SourceFileName,
    int ChunkIndex,
    string Text);
