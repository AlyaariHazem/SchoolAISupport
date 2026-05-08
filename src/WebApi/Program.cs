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

// --- Support tickets (escalation) ---
app.MapPost("/api/support/requests", (CreateSupportTicketRequest body, SupportTicketStore store) =>
{
    var errors = SupportTicketRequestValidator.Validate(body);
    if (!SupportTicketRequestValidator.IsValid(errors))
    {
        return Results.ValidationProblem(errors);
    }

    var ticket = store.Create(body);
    return Results.Created($"/api/support/requests/{ticket.Id}", ticket);
})
    .WithName("CreateSupportTicket")
    .Produces<SupportTicket>(StatusCodes.Status201Created)
    .ProducesValidationProblem();

app.MapGet("/api/support/requests/{id}", (string id, SupportTicketStore store) =>
{
    var ticket = store.GetById(id);
    return ticket is null ? Results.NotFound() : Results.Ok(ticket);
})
    .WithName("GetSupportTicketById")
    .Produces<SupportTicket>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

// Admin: list all tickets (header X-User-Role: admin)
app.MapGet("/api/admin/support-requests", (HttpRequest http, SupportTicketStore store) =>
{
    if (!AdminApiHelper.IsAdmin(http))
    {
        return Results.Json(
            new
            {
                error = "Admin access required.",
                hint = $"Send header {AdminApiHelper.UserRoleHeaderName}: admin"
            },
            statusCode: StatusCodes.Status403Forbidden);
    }

    return Results.Ok(store.ListAllOrderedByCreatedDesc());
})
    .WithName("ListSupportTicketsAdmin")
    .Produces<IReadOnlyList<SupportTicket>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden);

// --- Quiz generation ---
app.MapPost("/api/tools/generate-quiz", async (GenerateQuizRequest body, QuizGenerationService quiz, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Topic) && string.IsNullOrWhiteSpace(body.SourceText))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["topic"] = ["Provide Topic and/or SourceText."],
            ["sourceText"] = ["Provide Topic and/or SourceText."]
        });
    }

    try
    {
        var result = await quiz.GenerateAsync(body, ct);
        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "Quiz generation failed",
            detail: ex.Message);
    }
})
    .WithName("GenerateQuiz")
    .Produces<GenerateQuizResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status502BadGateway);

// --- Summarize school document ---
app.MapPost("/api/tools/summarize-document", async (SummarizeSchoolDocumentRequest body, SchoolDocumentSummarizationService summarizer, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Text))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(SummarizeSchoolDocumentRequest.Text)] = ["Text is required."]
        });
    }

    try
    {
        var summary = await summarizer.SummarizeAsync(body.Text, ct);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Empty summary",
                detail: "The model returned no summary text.");
        }

        return Results.Ok(new SummarizeSchoolDocumentResponse(summary));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "Summarization failed",
            detail: ex.Message);
    }
})
    .WithName("SummarizeSchoolDocument")
    .Produces<SummarizeSchoolDocumentResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status502BadGateway);

app.Run();
