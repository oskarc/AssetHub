using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AssetHub.Api.Endpoints;

public static class MigrationEndpoints
{
    public static void MapMigrationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/migrations")
            .RequireAuthorization(Constants.Policies.RequireAdmin)
            .WithTags("Migrations");

        group.MapPost("", CreateMigration)
            .AddEndpointFilter<ValidationFilter<CreateMigrationDto>>()
            .DisableAntiforgery()
            .WithName("CreateMigration");

        group.MapGet("", ListMigrations)
            .WithName("ListMigrations");

        group.MapGet("{id:guid}", GetMigration)
            .WithName("GetMigration");

        group.MapPost("{id:guid}/manifest", UploadManifest)
            .DisableAntiforgery()
            .WithName("UploadMigrationManifest");

        group.MapPost("{id:guid}/start", StartMigration)
            .DisableAntiforgery()
            .WithName("StartMigration");

        group.MapPost("{id:guid}/s3/scan", StartS3Scan)
            .DisableAntiforgery()
            .WithName("StartMigrationS3Scan");

        group.MapPost("{id:guid}/cancel", CancelMigration)
            .DisableAntiforgery()
            .WithName("CancelMigration");

        group.MapPost("{id:guid}/retry", RetryFailedItems)
            .DisableAntiforgery()
            .WithName("RetryFailedMigrationItems");

        group.MapGet("{id:guid}/progress", GetMigrationProgress)
            .WithName("GetMigrationProgress");

        group.MapGet("{id:guid}/items", GetMigrationItems)
            .WithName("GetMigrationItems");

        group.MapGet("{id:guid}/outcome.csv", DownloadOutcomeCsv)
            .WithName("DownloadMigrationOutcomeCsv");

        group.MapDelete("{id:guid}", DeleteMigration)
            .DisableAntiforgery()
            .WithName("DeleteMigration");

        group.MapDelete("bulk", BulkDeleteMigrations)
            .DisableAntiforgery()
            .WithName("BulkDeleteMigrations");

        group.MapPost("{id:guid}/files", UploadStagingFiles)
            .DisableAntiforgery()
            .WithName("UploadMigrationStagingFiles");

        group.MapDelete("{id:guid}/items/{itemId:guid}/unstage", UnstageMigrationItem)
            .DisableAntiforgery()
            .WithName("UnstageMigrationItem");
    }

    private static async Task<IResult> CreateMigration(
        CreateMigrationDto dto,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        return (await svc.CreateAsync(dto, ct))
            .ToHttpResult(value => Results.Created($"/api/v1/admin/migrations/{value.Id}", value));
    }

    private static async Task<IResult> ListMigrations(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        [FromServices] IMigrationService? svc = null,
        CancellationToken ct = default)
    {
        return (await svc!.ListAsync(skip, take, ct)).ToHttpResult();
    }

    private static async Task<IResult> GetMigration(
        Guid id,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        return (await svc.GetByIdAsync(id, ct)).ToHttpResult();
    }

    private static async Task<IResult> UploadManifest(
        Guid id,
        HttpRequest request,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        if (!request.HasFormContentType || request.Form.Files.Count == 0)
            return Results.BadRequest(new ApiError
            {
                Code = "BAD_REQUEST",
                Message = "A CSV file is required."
            });

        var file = request.Form.Files[0];
        if (file.Length == 0)
            return Results.BadRequest(new ApiError
            {
                Code = "BAD_REQUEST",
                Message = "The uploaded file is empty."
            });

        using var stream = file.OpenReadStream();
        return (await svc.UploadManifestAsync(id, stream, ct)).ToHttpResult();
    }

    private static async Task<IResult> StartMigration(
        Guid id,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        return (await svc.StartAsync(id, ct)).ToHttpResult();
    }

    private static async Task<IResult> StartS3Scan(
        Guid id,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        return (await svc.StartS3ScanAsync(id, ct)).ToHttpResult();
    }

    private static async Task<IResult> CancelMigration(
        Guid id,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        return (await svc.CancelAsync(id, ct)).ToHttpResult();
    }

    private static async Task<IResult> RetryFailedItems(
        Guid id,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        return (await svc.RetryFailedAsync(id, ct)).ToHttpResult();
    }

    private static async Task<IResult> GetMigrationProgress(
        Guid id,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        return (await svc.GetProgressAsync(id, ct)).ToHttpResult();
    }

    private static async Task<IResult> GetMigrationItems(
        Guid id,
        [FromQuery] string? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromServices] IMigrationService? svc = null,
        CancellationToken ct = default)
    {
        return (await svc!.GetItemsAsync(id, status, skip, take, ct)).ToHttpResult();
    }

    private static async Task<IResult> DownloadOutcomeCsv(
        Guid id,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        // Get all items for this migration (no pagination — full report)
        var result = await svc.GetItemsAsync(id, null, 0, 100_000, ct);
        if (!result.IsSuccess)
            return result.ToHttpResult();

        var items = result.Value!.Items;
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("external_id,filename,status,target_asset_id,error_code,error_message");

        foreach (var item in items)
        {
            csv.Append(EscapeCsvField(item.ExternalId ?? ""));
            csv.Append(',');
            csv.Append(EscapeCsvField(item.FileName));
            csv.Append(',');
            csv.Append(EscapeCsvField(item.Status));
            csv.Append(',');
            csv.Append(item.AssetId?.ToString() ?? "");
            csv.Append(',');
            csv.Append(EscapeCsvField(item.ErrorCode ?? ""));
            csv.Append(',');
            csv.Append(EscapeCsvField(item.ErrorMessage ?? ""));
            csv.AppendLine();
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        return Results.File(bytes, "text/csv", $"migration-{id}-outcome.csv");
    }

    private static async Task<IResult> DeleteMigration(
        Guid id,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        return (await svc.DeleteAsync(id, ct)).ToHttpResult();
    }

    private static async Task<IResult> UnstageMigrationItem(
        Guid id,
        Guid itemId,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        return (await svc.UnstageMigrationItemAsync(id, itemId, ct)).ToHttpResult();
    }

    private static async Task<IResult> BulkDeleteMigrations(
        [FromQuery] string filter,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        return (await svc.BulkDeleteAsync(filter, ct)).ToHttpResult();
    }

    private static string EscapeCsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static async Task<IResult> UploadStagingFiles(
        Guid id,
        HttpRequest request,
        [FromServices] IMigrationService svc,
        CancellationToken ct)
    {
        if (!request.HasFormContentType || request.Form.Files.Count == 0)
            return Results.BadRequest(new ApiError
            {
                Code = "BAD_REQUEST",
                Message = "At least one file is required."
            });

        var files = new List<(string FileName, Stream Stream, string ContentType)>();
        try
        {
            foreach (var f in request.Form.Files)
                files.Add((f.FileName, f.OpenReadStream(), f.ContentType ?? "application/octet-stream"));

            return (await svc.UploadStagingFilesAsync(id, files, ct)).ToHttpResult();
        }
        finally
        {
            foreach (var (_, stream, _) in files)
                await stream.DisposeAsync();
        }
    }
}
