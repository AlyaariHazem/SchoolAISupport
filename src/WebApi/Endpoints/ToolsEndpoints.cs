using WebApi.Models;
using WebApi.Services;

namespace WebApi.Endpoints;

/// <summary>
/// LLM-backed utilities: quiz generation and document summarization.
/// </summary>
public static class ToolsEndpoints
{
    public static void MapToolsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/tools/generate-quiz", async (GenerateQuizRequest body, QuizGenerationService quiz, OpenAiLlmAvailability openAi, CancellationToken ct) =>
            {
                if (!openAi.IsConfigured)
                {
                    return Results.Json(
                        new { error = openAi.ConfigurationHint },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

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
            .WithTags("Tools")
            .WithSummary("Generate quiz questions")
            .WithDescription("From a topic and/or source text. Requires OPENAI_API_KEY.")
            .Produces<GenerateQuizResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        app.MapPost("/api/tools/summarize-document", async (SummarizeSchoolDocumentRequest body, SchoolDocumentSummarizationService summarizer, OpenAiLlmAvailability openAi, CancellationToken ct) =>
            {
                if (!openAi.IsConfigured)
                {
                    return Results.Json(
                        new { error = openAi.ConfigurationHint },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

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
            .WithTags("Tools")
            .WithSummary("Summarize school document text")
            .WithDescription("Condenses user-provided school text. Does not invent policy. Requires OPENAI_API_KEY.")
            .Produces<SummarizeSchoolDocumentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}
