# PeopleDesk — Intelligent Employee Document Assistant

An Angular chat app and ASP.NET Core API that answer employee questions using company documents. The API embeds the question, runs hybrid keyword + vector search in Azure AI Search, and asks a Microsoft Foundry chat model to answer only from retrieved passages. Every answer includes source citations.

## Architecture

```text
Angular chat UI → ASP.NET Core API → Microsoft Foundry chat + embedding models
                                  → Azure AI Search (hybrid keyword/vector)
Document upload → ASP.NET Core API → private Azure Blob Storage
                                  → text extraction/chunking/embeddings → Search index
PDF uploads → Azure AI Document Intelligence (read/OCR)
```

## Requirements

- Node.js 20.19+ and npm
- .NET 8 SDK
- Azure AI Foundry project with a chat deployment and an embedding deployment (1536 dimensions, for example `text-embedding-3-small`)
- Azure AI Search service
- Azure Storage account
- For PDF ingestion: Azure AI Document Intelligence
- Azure CLI login for local passwordless authentication (`az login`)

## Configure Azure services

1. Create a Foundry chat model deployment and an embedding deployment. The configured Foundry endpoint must be the Azure OpenAI-compatible endpoint for the project/resource.
2. Create an Azure AI Search service and an Azure Storage account.
3. Assign the local developer identity the required roles:
   - Foundry: **Cognitive Services OpenAI User**
   - Search: **Search Index Data Contributor** and **Search Service Contributor**
   - Storage: **Storage Blob Data Contributor**
   - Document Intelligence (PDF only): **Cognitive Services User**
4. Configure API settings using .NET user-secrets (kept outside the repository):

   ```bash
   cd api/EmployeeDocumentAssistant.Api
   dotnet user-secrets init
   dotnet user-secrets set "Assistant:FoundryEndpoint" "https://YOUR-FOUNDRY-ENDPOINT/"
   dotnet user-secrets set "Assistant:ChatDeployment" "YOUR-CHAT-DEPLOYMENT"
   dotnet user-secrets set "Assistant:EmbeddingDeployment" "YOUR-EMBEDDING-DEPLOYMENT"
   dotnet user-secrets set "Assistant:SearchEndpoint" "https://YOUR-SEARCH-SERVICE.search.windows.net"
   dotnet user-secrets set "Assistant:BlobServiceUri" "https://YOUR-STORAGE-ACCOUNT.blob.core.windows.net"
   # Optional, only needed for PDF uploads:
   dotnet user-secrets set "Assistant:DocumentIntelligenceEndpoint" "https://YOUR-DOCUMENT-INTELLIGENCE.cognitiveservices.azure.com/"
   ```

The Search index is created automatically when the first document is uploaded. Search vector dimensions must match the embedding deployment.

## Provision Azure infrastructure with AZD

The repository includes AZD + Bicep infrastructure for the Free Static Web App, Linux App Service API, Foundry model deployments, Azure AI Search, private document storage, Document Intelligence, managed identity/RBAC, and workspace-based monitoring.

1. Install the Azure Developer CLI, Azure CLI, and Bicep; sign in with `az login`.
2. Create and configure an AZD environment:

   ```bash
   azd env new employee-doc-assistant-dev
   azd env set AZURE_SUBSCRIPTION_ID "<your-subscription-id>"
   azd env set AZURE_LOCATION centralindia
   ```

3. Run `azd provision` to create Azure resources only. Run `azd deploy` to publish the Angular and API services, or `azd up` to do both.

The Angular app reads its API origin from a runtime configuration asset generated during AZD packaging; the API CORS policy is set to the deployed Static Web App origin. Azure model availability/capacity, region support, subscription quota, and policy must be checked before provisioning. The template does not include a subscription ID or credentials.

## Run locally

In one terminal:

```bash
cd api/EmployeeDocumentAssistant.Api
dotnet run --urls http://localhost:5080
```

In another terminal from the project root:

```bash
npm install
npm start
```

Open [http://localhost:4200](http://localhost:4200). The Angular dev server proxies `/api` requests to the API on port 5080.

## Add HR documents

Use the API Swagger page at `http://localhost:5080/swagger` in Development, or upload a PDF, TXT, or Markdown file:

```bash
curl -X POST "http://localhost:5080/api/documents?category=Benefits" \
  -F "file=@./parental-leave-policy.pdf"
```

Supported categories include `Benefits`, `People & culture`, and `Workplace`. The uploaded original is stored in a private Blob container; extracted text is chunked, embedded, and indexed for retrieval. PDFs use Document Intelligence; TXT and Markdown are read directly.

## API

- `POST /api/chat` — JSON `{ "question": "...", "category": "Benefits" }`; category is optional. Responds with `{ "answer": "...", "citations": [{ "sourceName": "...", "sourceUrl": "...", "excerpt": "..." }] }`.
- `POST /api/documents?category=Benefits` — multipart form upload with a `file` field.
- `GET /api/health` — liveness check.

Azure credentials are used only by the API. Local development uses `DefaultAzureCredential`; Azure-hosted environments use managed identity. Do not put keys or secrets in Angular configuration.

## Production notes

- Configure the API's `AllowedOrigins` with the deployed Angular origin.
- Grant the API managed identity the same least-privilege data roles described above; use a user-assigned identity by setting `Assistant:ManagedIdentityClientId` if needed.
- Put the API behind organizational authentication and enforce employee authorization before exposing HR content. Apply document-level access controls in search if policies differ by employee group.
- Add retention, malware scanning, upload quotas, and audit logging according to company policy before production use.
- Provision the Foundry model and Search vector dimensions consistently; the starter index uses 1,536-dimensional embeddings.
