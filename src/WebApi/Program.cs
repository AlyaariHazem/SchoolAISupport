using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using WebApi.Models;
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

// 1. Define the variables we extracted from Microsoft Foundry
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5-mini";

// 2. Instantiate the universal chat client with OpenTelemetry GenAI instrumentation
IChatClient chatClient = new AzureOpenAIClient(
        new Uri(endpoint),
        new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(configure: c => c.EnableSensitiveData = true)
    .Build();
builder.Services.AddSingleton(chatClient);

// 3. Register school support services (kept intentionally lightweight and easy to extend)
var promptBuilder = new PromptBuilderService();
builder.Services.AddSingleton(promptBuilder);
builder.Services.AddSingleton<ConversationMemoryService>();
builder.Services.AddSingleton<SchoolKnowledgeService>();
builder.Services.AddSingleton<SupportRequestService>();
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

// Map DevUI endpoints 
app.MapDevUI();
app.MapOpenAIResponses();
app.MapOpenAIConversations();

// School AI Support chat: validated input, bounded message size, structured success payload.
app.MapPost("/api/chat", async (
    ChatRequest request,
    AgentService agentService,
    CancellationToken cancellationToken,
    ILoggerFactory loggerFactory) =>
{
    var log = loggerFactory.CreateLogger("SchoolChat");

    var trimmedRequest = request with
    {
        Message = request.Message.Trim(),
        ConversationId = string.IsNullOrWhiteSpace(request.ConversationId) ? null : request.ConversationId.Trim(),
        UserName = string.IsNullOrWhiteSpace(request.UserName) ? null : request.UserName.Trim()
    };

    var validationErrors = ChatRequestValidator.Validate(trimmedRequest);
    if (!ChatRequestValidator.IsValid(validationErrors))
    {
        return Results.ValidationProblem(validationErrors);
    }

    // Normalize conversation id: reuse client id or start a new thread.
    var conversationId = string.IsNullOrWhiteSpace(trimmedRequest.ConversationId)
        ? Guid.NewGuid().ToString("N")
        : trimmedRequest.ConversationId;

    var normalizedRequest = trimmedRequest with { ConversationId = conversationId };

    try
    {
        var responseText = await agentService.GetResponseAsync(normalizedRequest, cancellationToken);
        return Results.Ok(new SchoolChatResponse(conversationId, responseText));
    }
    catch (OperationCanceledException)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status499ClientClosedRequest,
            title: "Request cancelled",
            detail: "The client closed the connection before the assistant finished.");
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Assistant failed for conversation {ConversationId}", conversationId);
        return Results.Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "Assistant unavailable",
            detail: "The assistant could not complete your request. Please try again later.");
    }
})
    .WithName("SchoolChat")
    .Produces<SchoolChatResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status502BadGateway);

app.Run();
