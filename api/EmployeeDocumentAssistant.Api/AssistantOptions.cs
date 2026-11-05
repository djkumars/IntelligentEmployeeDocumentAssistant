namespace EmployeeDocumentAssistant.Api;

public sealed class AssistantOptions
{
    public string? FoundryEndpoint { get; set; }
    public string ChatDeployment { get; set; } = "gpt-4o-mini";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
    public string? SearchEndpoint { get; set; }
    public string SearchIndexName { get; set; } = "employee-knowledge";
    public string? BlobServiceUri { get; set; }
    public string BlobContainerName { get; set; } = "hr-documents";
    public string? DocumentIntelligenceEndpoint { get; set; }
    public string? ManagedIdentityClientId { get; set; }
}

public sealed record ChatRequest(string Question, string? Category);
public sealed record Citation(string SourceName, string SourceUrl, string Excerpt);
public sealed record ChatResponse(string Answer, IReadOnlyList<Citation> Citations);
public sealed record IngestionResponse(string FileName, int ChunksIndexed, string Category);
