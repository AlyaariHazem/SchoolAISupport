# minimal-agent

A minimal ASP.NET Core Web API project for AI agent development. The goal is to represent agent interactions visually and trace **all events** during GenAI integration — including system prompts, tokens, model responses, tool calling, and more.

- **DevUI** — embedded visual dashboard for inspecting agent events in real-time
- **Aspire Dashboard** — OpenTelemetry-powered distributed tracing across the full request pipeline
- Uses `AddAIAgent` + `MapPost("/api/chat")` for a single-endpoint agent setup you can test immediately

---

## Solution Structure

```
src/
├── AppHost/           # .NET Aspire orchestrator
├── ServiceDefaults/   # Shared resilience, service-discovery & OpenTelemetry config
└── WebApi/            # Minimal API hosting the AI agent, DevUI, and chat endpoint
```

---

## 0 — Aspire Setup

The solution uses **.NET Aspire** (`Aspire.AppHost.Sdk/13.2.1`) targeting `net10.0`.  
The AppHost orchestrates the WebApi project and wires up the Aspire Dashboard automatically for distributed tracing and logging.

---

## 1 — AppHost

`src/AppHost/AppHost.cs` registers the WebApi project and adds a custom **DevUI** URL to the Aspire dashboard:

```csharp
builder.AddProject<Projects.WebApi>("webapi")
    .WithUrls(context =>
    {
        var baseUrl = context.Urls.FirstOrDefault();
        if (baseUrl is not null)
        {
            context.Urls.Add(new()
            {
                Url = baseUrl.Url.TrimEnd('/') + "/devui",
                DisplayText = "DevUI Visual App"
            });
        }
    });
```

`WithUrls` appends a `/devui` link next to the default endpoint in the Aspire dashboard, so you can jump straight to the DevUI visual app without remembering the path.

---

## 2 — ServiceDefaults

`src/ServiceDefaults/Extensions.cs` provides shared infrastructure for every service in the solution.

### Extended HTTP Resilience (tuned for LLM calls)

```csharp
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(3);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(6);
        options.Retry.MaxRetryAttempts = 2;
    });
});
```

Timeouts are set much higher than defaults because LLM completions can take tens of seconds. The circuit breaker sampling window (`6 min`) exceeds the total timeout (`5 min`) so a single slow call doesn't trip the breaker.

### OpenTelemetry Trace Sources

```csharp
.WithTracing(tracing =>
{
    tracing.AddSource(builder.Environment.ApplicationName)
        .AddSource("*Microsoft.Extensions.AI")
        .AddSource("*Microsoft.Extensions.Agents*")
```

These wildcard trace sources capture **every span** emitted by `Microsoft.Extensions.AI` and `Microsoft.Extensions.Agents`, which means system prompts, token counts, model responses, and tool-call events all appear in the Aspire Dashboard traces.

---

## 3 — WebApi

### Packages

| Package | Purpose |
|---|---|
| `Azure.Identity` | `AzureCliCredential` for local dev auth |
| `Azure.AI.OpenAI` | Azure OpenAI client SDK |
| `Microsoft.Agents.AI.OpenAI` | Agent ↔ OpenAI bridge |
| `Microsoft.Agents.AI.DevUI` | Embedded visual dashboard |
| `Microsoft.Agents.AI.Hosting` | `AddAIAgent` / `AIAgent` hosting |
| `Microsoft.Agents.AI.Hosting.OpenAI` | OpenAI-specific hosting (responses & conversations) |

### Program.cs — step by step

**① Environment variables**

```csharp
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
    ?? "gpt-5-mini";
```

`AZURE_OPENAI_ENDPOINT` is required; `AZURE_OPENAI_DEPLOYMENT_NAME` defaults to `gpt-5-mini`.

**② Chat client with OpenTelemetry**

```csharp
IChatClient chatClient = new AzureOpenAIClient(
        new Uri(endpoint),
        new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(configure: c => c.EnableSensitiveData = true)
    .Build();
```

Builds a `Microsoft.Extensions.AI.IChatClient` from the Azure OpenAI SDK. `.UseOpenTelemetry(EnableSensitiveData = true)` ensures prompt text, completion text, and token usage are all emitted as trace attributes — visible in the Aspire Dashboard.

**③ Agent registration**

```csharp
builder.AddAIAgent(
    name: "NetworkSupportAgent",
    instructions: "...",
    chatClient);
```

Registers a keyed `AIAgent` named `"NetworkSupportAgent"` with a Tier 1 IT Support persona. The agent is resolved via `[FromKeyedServices("NetworkSupportAgent")]`.

**④ DevUI + OpenAI middleware**

```csharp
builder.AddDevUI();
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();
// ...
app.MapDevUI();
app.MapOpenAIResponses();
app.MapOpenAIConversations();
```

`AddDevUI` / `MapDevUI` provides the visual event inspector at `/devui`. `AddOpenAIResponses` / `AddOpenAIConversations` wire up the OpenAI-compatible responses and conversation endpoints.

**⑤ Chat endpoint**

```csharp
app.MapPost("/api/chat", async (ChatRequest request,
    [FromKeyedServices("NetworkSupportAgent")] AIAgent networkSupportAgent) =>
{
    var response = await networkSupportAgent.RunAsync(request.Message);
    return Results.Ok(new { response = response.Text });
});
```

A single POST endpoint that accepts a JSON body `{ "message": "..." }`, runs the agent, and returns the response text.

### WebApi.http

A ready-to-use HTTP file for testing from Visual Studio:

```http
@WebApi_HostAddress = http://localhost:5043

POST {{WebApi_HostAddress}}/api/chat
Content-Type: application/json

{
  "message": "My VPN keeps disconnecting every few minutes. What should I try?"
}
```

Open `WebApi.http` in Visual Studio and click **Send Request** to test the agent without leaving the IDE.

---

## Testing End-to-End

1. **Set environment variables**
   ```
   AZURE_OPENAI_ENDPOINT=https://<your-resource>.openai.azure.com/
   AZURE_OPENAI_DEPLOYMENT_NAME=gpt-5-mini
   ```
2. **Run the Aspire AppHost** — start the `AppHost` project; it launches the WebApi and opens the Aspire Dashboard.
3. **Open DevUI** — click the **"DevUI Visual App"** link in the Aspire Dashboard (or navigate to `http://localhost:5043/devui`).
4. **Send a prompt** — use the `/api/chat` endpoint or `WebApi.http`:
   ```
   "My VPN keeps disconnecting every few minutes. What should I try?"
   ```
5. **Inspect DevUI** — see real-time agent events: system prompt, user message, model response, token usage, and tool calls.
6. **Inspect Aspire Dashboard** — open the **Traces** tab to see the full distributed trace including OpenTelemetry GenAI spans with prompt/completion text and token counts.
