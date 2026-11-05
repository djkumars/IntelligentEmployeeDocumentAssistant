using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace EmployeeDocumentAssistant.Api;

public sealed class DocumentIngestionService(
    BlobServiceClient blobService,
    AzureOpenAIClient foundry,
    KnowledgeIndex knowledgeIndex,
    IOptions<AssistantOptions> options,
    IWebHostEnvironment environment)
{
    public async Task<IngestionResponse> IngestAsync(IFormFile file, string category, CancellationToken cancellationToken)
    {
        var config = options.Value;
        var safeFileName = Path.GetFileName(file.FileName);

        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        var text = await ExtractTextAsync(bytes, Path.GetExtension(safeFileName), cancellationToken);
        var chunks = SplitIntoChunks(text);
        if (chunks.Count == 0) throw new InvalidOperationException("No readable text was found in the document.");

        var container = blobService.GetBlobContainerClient(config.BlobContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        var blob = container.GetBlobClient($"{Guid.NewGuid():N}/{safeFileName}");
        await using (var upload = new MemoryStream(bytes, writable: false))
        {
            await blob.UploadAsync(upload, new BlobHttpHeaders { ContentType = file.ContentType }, cancellationToken: cancellationToken);
        }

        var embeddingClient = foundry.GetEmbeddingClient(config.EmbeddingDeployment);
        async Task<float[]> Embed(string content, CancellationToken token)
        {
            var embedding = await embeddingClient.GenerateEmbeddingAsync(content, cancellationToken: token);
            return embedding.Value.ToFloats().ToArray();
        }

        await knowledgeIndex.IndexChunksAsync(safeFileName, blob.Uri.ToString(), category, chunks, Embed, cancellationToken);
        return new IngestionResponse(safeFileName, chunks.Count, category);
    }

    private async Task<string> ExtractTextAsync(byte[] bytes, string extension, CancellationToken cancellationToken)
    {
        if (!extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return System.Text.Encoding.UTF8.GetString(bytes);

        var endpoint = options.Value.DocumentIntelligenceEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("Set Assistant:DocumentIntelligenceEndpoint to ingest PDF files. TXT and Markdown files do not need OCR.");

        var credential = environment.IsDevelopment()
            ? new DefaultAzureCredential()
            : string.IsNullOrWhiteSpace(options.Value.ManagedIdentityClientId)
                ? new ManagedIdentityCredential()
                : new ManagedIdentityCredential(options.Value.ManagedIdentityClientId);
        var client = new DocumentIntelligenceClient(new Uri(endpoint), credential);
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-read",
            BinaryData.FromBytes(bytes),
            cancellationToken: cancellationToken);
        return string.Join("\n", operation.Value.Pages
            .SelectMany(page => page.Lines)
            .Select(line => line.Content));
    }

    private static IReadOnlyList<string> SplitIntoChunks(string text)
    {
        const int chunkSize = 1200;
        const int overlap = 180;
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var chunks = new List<string>();
        for (var start = 0; start < normalized.Length;)
        {
            var end = Math.Min(start + chunkSize, normalized.Length);
            if (end < normalized.Length)
            {
                var boundary = normalized.LastIndexOf(' ', end, end - start);
                if (boundary > start + chunkSize / 2) end = boundary;
            }
            chunks.Add(normalized[start..end].Trim());
            if (end == normalized.Length) break;
            start = end - overlap;
        }
        return chunks;
    }
}
