using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.OpenApi;
using OpenAI;
using OpenAI.Chat;
using WebApi.Endpoints;
using WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

// Minimal APIs: camelCase properties and enum strings (e.g. userRole: "student").
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

// OpenAPI / Swagger — browse all HTTP endpoints at /swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "School AI Support Agent",
        Version = "v1",
        Description =
            """
            REST API for the school assistant: **Chat**, **Support tickets**, and **Tools** (quiz + summarize).
            Agent debugging UI: `/devui`. OpenTelemetry traces: Aspire dashboard when using AppHost.
            LLM routes require `OPENAI_API_KEY` (503 with a clear message if missing).
            """
    });
});

// 1. OpenAI API (standard) — key optional so the app can start and return friendly errors from HTTP endpoints.
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim();
if (string.IsNullOrEmpty(openAiModel))
{
    openAiModel = "gpt-4o-mini";
}

IChatClient chatClient;
if (!string.IsNullOrEmpty(openAiApiKey))
{
    chatClient = new OpenAIClient(openAiApiKey)
        .GetChatClient(openAiModel)
        .AsIChatClient()
        .AsBuilder()
        .UseOpenTelemetry(configure: c => c.EnableSensitiveData = true)
        .Build();
    builder.Services.AddSingleton(new OpenAiLlmAvailability { IsConfigured = true, ConfigurationHint = "" });
}
else
{
    chatClient = new UnconfiguredOpenAiChatClient();
    builder.Services.AddSingleton(new OpenAiLlmAvailability
    {
        IsConfigured = false,
        ConfigurationHint =
            "OpenAI is not configured. Set the OPENAI_API_KEY environment variable to a valid API key (and optionally OPENAI_MODEL, default gpt-4o-mini), then restart the application."
    });
}

builder.Services.AddSingleton(chatClient);

// 3. Register school support services (kept intentionally lightweight and easy to extend)
var promptBuilder = new PromptBuilderService();
builder.Services.AddSingleton(promptBuilder);
builder.Services.AddSingleton<ConversationMemoryService>();
builder.Services.AddSingleton<SchoolKnowledgeService>();
builder.Services.AddHostedService<SchoolKnowledgeStartupLoader>();
builder.Services.AddSingleton<SupportRequestService>();
builder.Services.AddSingleton<SupportTicketStore>();
builder.Services.AddSingleton<QuizGenerationService>();
builder.Services.AddSingleton<SchoolDocumentSummarizationService>();
builder.Services.AddScoped<AgentService>();

// 4. Define and Register the AI agent
builder.AddAIAgent(
    name: "NetworkSupportAgent",
    instructions: promptBuilder.BuildSystemInstructions(),
    chatClient);

// 5. Register DevUI services
builder.AddDevUI();
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();


var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "School AI Support Agent v1");
        options.DocumentTitle = "School AI Support Agent — API";
    });

    // Quick entry from root when running locally
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

// Map DevUI endpoints
app.MapDevUI();
app.MapOpenAIResponses();
app.MapOpenAIConversations();

app.MapSchoolAgentApi();

app.Run();
