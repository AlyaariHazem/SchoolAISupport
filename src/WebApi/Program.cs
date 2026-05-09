using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.OpenApi;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using WebApi.Configuration;
using WebApi.Endpoints;
using WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Development: pick up OpenAI:ApiKey from MySchool Backend when both repos share the repo root (…/School/MySchool/Backend).
if (builder.Environment.IsDevelopment())
{
    var backendDevSettings = Path.GetFullPath(Path.Combine(
        builder.Environment.ContentRootPath,
        "..", "..", "..", "MySchool", "Backend", "appsettings.Development.json"));
    if (File.Exists(backendDevSettings))
        builder.Configuration.AddJsonFile(backendDevSettings, optional: true, reloadOnChange: true);
}

builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));

// Browser clients: in Development, allow any localhost / 127.0.0.1 origin (any port — e.g. ng serve on 4700).
// In non-Development, set Cors:Origins in appsettings / env.
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("SchoolPortal", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy
                .SetIsOriginAllowed(static origin =>
                {
                    if (string.IsNullOrEmpty(origin)) return false;
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
                    return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                        || uri.Host == "127.0.0.1";
                })
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else if (corsOrigins is { Length: > 0 })
        {
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });
});

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
            LLM routes require **OpenAI** settings (`OpenAI:ApiKey` in appsettings, User Secrets, or env `OPENAI__API_KEY`).
            """
    });
});

// 1. OpenAI — same keys as MySchool Backend (`OpenAI:ApiKey`, `OpenAI:Model`) plus env fallbacks.
var openAiOpts = builder.Configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>() ?? new OpenAiOptions();

var openAiApiKey = openAiOpts.ApiKey?.Trim();
if (string.IsNullOrEmpty(openAiApiKey))
    openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();

var openAiModel = openAiOpts.Model?.Trim();
if (string.IsNullOrEmpty(openAiModel))
    openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim();
if (string.IsNullOrEmpty(openAiModel))
    openAiModel = "gpt-4o-mini";

var configuredBaseUrl = openAiOpts.BaseUrl?.Trim();

IChatClient chatClient;
if (!string.IsNullOrEmpty(openAiApiKey))
{
    var credential = new ApiKeyCredential(openAiApiKey);
    OpenAIClient openAiClient;
    if (!string.IsNullOrEmpty(configuredBaseUrl)
        && Uri.TryCreate(configuredBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var endpointUri))
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = endpointUri };
        openAiClient = new OpenAIClient(credential, clientOptions);
    }
    else
    {
        openAiClient = new OpenAIClient(credential);
    }

    chatClient = openAiClient
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
            """
            OpenAI is not configured. Set OpenAI:ApiKey in appsettings, user secrets (dotnet user-secrets set "OpenAI:ApiKey" "sk-..."),
            or environment OPENAI_API_KEY / OPENAI__API_KEY, then restart.
            """
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

app.UseCors("SchoolPortal");

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
