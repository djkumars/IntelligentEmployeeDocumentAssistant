using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Storage.Blobs;
using EmployeeDocumentAssistant.Api;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<AssistantOptions>(builder.Configuration.GetSection("Assistant"));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AssistantOptions>>().Value;
    var credential = CreateCredential(builder.Environment, options);
    return new AzureOpenAIClient(new Uri(Required(options.FoundryEndpoint, "Assistant:FoundryEndpoint")), credential);
});
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AssistantOptions>>().Value;
    var credential = CreateCredential(builder.Environment, options);
    return new SearchIndexClient(new Uri(Required(options.SearchEndpoint, "Assistant:SearchEndpoint")), credential);
});
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AssistantOptions>>().Value;
    var credential = CreateCredential(builder.Environment, options);
    return new SearchClient(new Uri(Required(options.SearchEndpoint, "Assistant:SearchEndpoint")), options.SearchIndexName, credential);
});
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AssistantOptions>>().Value;
    var credential = CreateCredential(builder.Environment, options);
    return new BlobServiceClient(new Uri(Required(options.BlobServiceUri, "Assistant:BlobServiceUri")), credential);
});
builder.Services.AddHttpClient();
builder.Services.AddSingleton<KnowledgeIndex>();
builder.Services.AddSingleton<AssistantService>();
builder.Services.AddSingleton<DocumentIngestionService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"])
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "employee-document-assistant" }));
app.MapPost("/api/chat", async (ChatRequest request, AssistantService assistant, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
        return Results.BadRequest(new { error = "Enter a question to search company documents." });
    if (request.Question.Length > 2000)
        return Results.BadRequest(new { error = "Questions must be 2,000 characters or fewer." });

    try
    {
        return Results.Ok(await assistant.AnswerAsync(request, cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
app.MapPost("/api/documents", async (IFormFile file, string category, DocumentIngestionService ingestion, CancellationToken cancellationToken) =>
{
    if (file.Length == 0) return Results.BadRequest(new { error = "Choose a non-empty document." });
    if (file.Length > 25 * 1024 * 1024) return Results.BadRequest(new { error = "Documents must be 25 MB or smaller." });
    var extension = Path.GetExtension(file.FileName);
    if (!new[] { ".pdf", ".txt", ".md" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Supported file types are PDF, TXT, and Markdown." });

    try
    {
        var result = await ingestion.IngestAsync(file, category, cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

static string Required(string? value, string settingName) =>
    !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException($"Configure {settingName} before starting the API.");

static Azure.Core.TokenCredential CreateCredential(IWebHostEnvironment environment, AssistantOptions options) =>
    environment.IsDevelopment()
        ? new DefaultAzureCredential()
        : string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
            ? new ManagedIdentityCredential()
            : new ManagedIdentityCredential(options.ManagedIdentityClientId);
