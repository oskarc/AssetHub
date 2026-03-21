using AssetHub.Api.Extensions;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class LogAnalysisEndpoints
{
    public static void MapLogAnalysisEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/log-analysis")
            .RequireAuthorization("RequireViewer")
            .WithTags("LogAnalysis");

        group.MapPost("analyze", AnalyzeLog)
            .DisableAntiforgery()
            .WithName("AnalyzeLog")
            .Accepts<IFormFile>("multipart/form-data");
    }

    private static async Task<IResult> AnalyzeLog(
        HttpRequest request,
        [FromServices] ILogAnalysisService svc,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new { code = "BAD_REQUEST", message = "Request must be multipart/form-data." });

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");

        if (file is null || file.Length == 0)
            return Results.BadRequest(new { code = "BAD_REQUEST", message = "No file was provided." });

        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".log", ".txt", ".ndjson", ".jsonl" };
        var ext = Path.GetExtension(file.FileName);
        if (!allowedExtensions.Contains(ext))
            return Results.BadRequest(new { code = "BAD_REQUEST", message = $"File type '{ext}' is not supported. Allowed: .log, .txt, .ndjson, .jsonl" });

        await using var stream = file.OpenReadStream();
        var result = await svc.AnalyzeAsync(stream, file.FileName, ct);
        return result.ToHttpResult();
    }
}
