using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;

namespace EmployeeDocumentAssistant.Api;

public sealed class KnowledgeIndex(
    SearchIndexClient indexClient,
    SearchClient searchClient,
    IOptions<AssistantOptions> options)
{
    private const int EmbeddingDimensions = 1536;
    private const string VectorProfileName = "hr-vector-profile";

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        var index = new SearchIndex(options.Value.SearchIndexName)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                new SearchableField("content") { IsFilterable = false },
                new SimpleField("sourceName", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("sourceUrl", SearchFieldDataType.String),
                new SimpleField("category", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("chunkIndex", SearchFieldDataType.Int32),
                new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    VectorSearchDimensions = EmbeddingDimensions,
                    VectorSearchProfileName = VectorProfileName
                }
            },
            VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration("hr-hnsw") },
                Profiles = { new VectorSearchProfile(VectorProfileName, "hr-hnsw") }
            }
        };
        await indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);
    }

    public async Task IndexChunksAsync(
        string sourceName,
        string sourceUrl,
        string category,
        IReadOnlyList<string> chunks,
        Func<string, CancellationToken, Task<float[]>> embed,
        CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        const int batchSize = 500;
        for (var batchStart = 0; batchStart < chunks.Count; batchStart += batchSize)
        {
            var documents = new List<SearchDocument>(Math.Min(batchSize, chunks.Count - batchStart));
            var batchEnd = Math.Min(batchStart + batchSize, chunks.Count);
            for (var index = batchStart; index < batchEnd; index++)
            {
                var content = chunks[index];
                documents.Add(new SearchDocument
                {
                    ["id"] = $"{Guid.NewGuid():N}-{index}",
                    ["content"] = content,
                    ["sourceName"] = sourceName,
                    ["sourceUrl"] = sourceUrl,
                    ["category"] = category,
                    ["chunkIndex"] = index,
                    ["contentVector"] = await embed(content, cancellationToken)
                });
            }
            await searchClient.MergeOrUploadDocumentsAsync(documents, cancellationToken: cancellationToken);
        }
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string question,
        string? category,
        Func<string, CancellationToken, Task<float[]>> embed,
        CancellationToken cancellationToken)
    {
        var vector = await embed(question, cancellationToken);
        var options = new SearchOptions { Size = 5 };
        options.SearchFields.Add("content");
        options.Select.Add("content");
        options.Select.Add("sourceName");
        options.Select.Add("sourceUrl");
        options.Select.Add("category");
        options.Filter = string.IsNullOrWhiteSpace(category) ? null : $"category eq '{EscapeOData(category)}'";
        options.VectorSearch = new VectorSearchOptions
        {
            Queries = { new VectorizedQuery(vector) { KNearestNeighborsCount = 5, Fields = { "contentVector" } } }
        };

        var response = await searchClient.SearchAsync<SearchDocument>(question, options, cancellationToken);
        var results = new List<RetrievedChunk>();
        await foreach (var result in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            var document = result.Document;
            results.Add(new RetrievedChunk(
                GetString(document, "content"),
                GetString(document, "sourceName"),
                GetString(document, "sourceUrl")));
        }
        return results;
    }

    private static string GetString(SearchDocument document, string key) =>
        document.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static string EscapeOData(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

public sealed record RetrievedChunk(string Content, string SourceName, string SourceUrl);
