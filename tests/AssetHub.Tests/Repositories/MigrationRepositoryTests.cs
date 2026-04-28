using System.Text.Json;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Tests.Repositories;

[Collection("Database")]
public class MigrationRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private MigrationRepository _repo = null!;

    public MigrationRepositoryTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        var dbName = _db.Database.GetDbConnection().Database!;
        _repo = new MigrationRepository(_fixture.CreateDbContextProvider(dbName));
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private static Migration MakeMigration(
        MigrationStatus status = MigrationStatus.Draft,
        string name = "Test migration",
        int total = 0)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            SourceType = MigrationSourceType.CsvUpload,
            Status = status,
            ItemsTotal = total,
            CreatedByUserId = "test-user-001",
            CreatedAt = DateTime.UtcNow
        };

    private static MigrationItem MakeItem(
        Guid migrationId,
        MigrationItemStatus status = MigrationItemStatus.Pending,
        bool staged = false,
        string? fileName = null,
        int rowNumber = 1)
        => new()
        {
            Id = Guid.NewGuid(),
            MigrationId = migrationId,
            Status = status,
            FileName = fileName ?? $"file-{Guid.NewGuid():N}.jpg",
            IdempotencyKey = Guid.NewGuid().ToString(),
            IsFileStaged = staged,
            RowNumber = rowNumber,
            CreatedAt = DateTime.UtcNow
        };

    // ── CreateAsync / GetByIdAsync ──────────────────────────────────

    [Fact]
    public async Task CreateAsync_PersistsMigrationWithJsonbFields()
    {
        var migration = MakeMigration();
        migration.SourceConfig["rowCount"] = 42;
        migration.FieldMapping["csv_title"] = "title";

        await _repo.CreateAsync(migration);

        var loaded = await _repo.GetByIdAsync(migration.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Test migration", loaded.Name);
        Assert.Equal(MigrationStatus.Draft, loaded.Status);
        // SourceConfig is Dictionary<string, object> — JSONB roundtrip yields JsonElement values.
        // FieldMapping is Dictionary<string, string> — values stay strings.
        Assert.Equal(42L, ((JsonElement)loaded.SourceConfig["rowCount"]).GetInt64());
        Assert.Equal("title", loaded.FieldMapping["csv_title"]);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _repo.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdWithItemsAsync_IncludesItems()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);
        await _repo.AddItemsAsync(new[]
        {
            MakeItem(migration.Id, rowNumber: 1),
            MakeItem(migration.Id, rowNumber: 2)
        });

        var loaded = await _repo.GetByIdWithItemsAsync(migration.Id);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Items.Count);
    }

    // ── ListAsync / CountAsync ───────────────────────────────────────

    [Fact]
    public async Task ListAsync_OrdersByCreatedAtDescending_WithPaging()
    {
        var older = MakeMigration(name: "Older");
        older.CreatedAt = DateTime.UtcNow.AddHours(-2);
        var newer = MakeMigration(name: "Newer");
        newer.CreatedAt = DateTime.UtcNow.AddHours(-1);
        var newest = MakeMigration(name: "Newest");
        await _repo.CreateAsync(older);
        await _repo.CreateAsync(newer);
        await _repo.CreateAsync(newest);

        var firstPage = await _repo.ListAsync(0, 2);
        Assert.Equal(2, firstPage.Count);
        Assert.Equal("Newest", firstPage[0].Name);
        Assert.Equal("Newer", firstPage[1].Name);

        var secondPage = await _repo.ListAsync(2, 2);
        Assert.Single(secondPage);
        Assert.Equal("Older", secondPage[0].Name);
    }

    [Fact]
    public async Task CountAsync_ReturnsTotalMigrationCount()
    {
        await _repo.CreateAsync(MakeMigration());
        await _repo.CreateAsync(MakeMigration());
        await _repo.CreateAsync(MakeMigration());

        Assert.Equal(3, await _repo.CountAsync());
    }

    // ── UpdateAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PersistsStatusAndCountChanges()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);

        migration.Status = MigrationStatus.Completed;
        migration.ItemsSucceeded = 10;
        migration.ItemsFailed = 2;
        migration.FinishedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(migration);

        var loaded = await _repo.GetByIdAsync(migration.Id);
        Assert.Equal(MigrationStatus.Completed, loaded!.Status);
        Assert.Equal(10, loaded.ItemsSucceeded);
        Assert.Equal(2, loaded.ItemsFailed);
        Assert.NotNull(loaded.FinishedAt);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_CascadesToItems()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);
        await _repo.AddItemsAsync(new[] { MakeItem(migration.Id), MakeItem(migration.Id) });

        await _repo.DeleteAsync(migration.Id);

        Assert.Null(await _repo.GetByIdAsync(migration.Id));
        Assert.Equal(0, await _repo.CountItemsAsync(migration.Id, null));
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_DoesNotThrow()
    {
        var ex = await Record.ExceptionAsync(() => _repo.DeleteAsync(Guid.NewGuid()));
        Assert.Null(ex);
    }

    // ── AddItemsAsync / GetItemsAsync ────────────────────────────────

    [Fact]
    public async Task AddItemsAsync_PersistsItems()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);

        var items = Enumerable.Range(1, 5).Select(i => MakeItem(migration.Id, rowNumber: i)).ToArray();
        await _repo.AddItemsAsync(items);

        var listed = await _repo.GetItemsAsync(migration.Id, null, 0, 100);
        Assert.Equal(5, listed.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, listed.Select(i => i.RowNumber).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_FiltersByStatus()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);
        await _repo.AddItemsAsync(new[]
        {
            MakeItem(migration.Id, MigrationItemStatus.Pending, rowNumber: 1),
            MakeItem(migration.Id, MigrationItemStatus.Failed, rowNumber: 2),
            MakeItem(migration.Id, MigrationItemStatus.Succeeded, rowNumber: 3),
            MakeItem(migration.Id, MigrationItemStatus.Failed, rowNumber: 4)
        });

        var failed = await _repo.GetItemsAsync(migration.Id, "failed", 0, 100);

        Assert.Equal(2, failed.Count);
        Assert.All(failed, i => Assert.Equal(MigrationItemStatus.Failed, i.Status));
    }

    [Fact]
    public async Task CountItemsAsync_FiltersByStatus()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);
        await _repo.AddItemsAsync(new[]
        {
            MakeItem(migration.Id, MigrationItemStatus.Pending),
            MakeItem(migration.Id, MigrationItemStatus.Succeeded),
            MakeItem(migration.Id, MigrationItemStatus.Succeeded),
            MakeItem(migration.Id, MigrationItemStatus.Failed)
        });

        Assert.Equal(4, await _repo.CountItemsAsync(migration.Id, null));
        Assert.Equal(2, await _repo.CountItemsAsync(migration.Id, "succeeded"));
        Assert.Equal(0, await _repo.CountItemsAsync(migration.Id, "skipped"));
    }

    // ── UpdateItemAsync / GetItemByIdAsync ───────────────────────────

    [Fact]
    public async Task UpdateItemAsync_PersistsStatusAndError()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);
        var item = MakeItem(migration.Id);
        await _repo.AddItemsAsync(new[] { item });

        item.Status = MigrationItemStatus.Failed;
        item.ErrorCode = "FILE_NOT_FOUND";
        item.ErrorMessage = "File missing from staging";
        item.AttemptCount = 2;
        item.ProcessedAt = DateTime.UtcNow;
        await _repo.UpdateItemAsync(item);

        var loaded = await _repo.GetItemByIdAsync(item.Id);
        Assert.NotNull(loaded);
        Assert.Equal(MigrationItemStatus.Failed, loaded.Status);
        Assert.Equal("FILE_NOT_FOUND", loaded.ErrorCode);
        Assert.Equal("File missing from staging", loaded.ErrorMessage);
        Assert.Equal(2, loaded.AttemptCount);
    }

    // ── Pending / Failed queries ─────────────────────────────────────

    [Fact]
    public async Task GetPendingItemsAsync_ReturnsOnlyPendingInRowOrder()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);
        await _repo.AddItemsAsync(new[]
        {
            MakeItem(migration.Id, MigrationItemStatus.Pending, rowNumber: 2),
            MakeItem(migration.Id, MigrationItemStatus.Succeeded, rowNumber: 1),
            MakeItem(migration.Id, MigrationItemStatus.Pending, rowNumber: 3),
            MakeItem(migration.Id, MigrationItemStatus.Failed, rowNumber: 4)
        });

        var pending = await _repo.GetPendingItemsAsync(migration.Id);

        Assert.Equal(2, pending.Count);
        Assert.All(pending, i => Assert.Equal(MigrationItemStatus.Pending, i.Status));
        Assert.Equal(new[] { 2, 3 }, pending.Select(i => i.RowNumber).ToArray());
    }

    [Fact]
    public async Task GetFailedItemsAsync_ReturnsOnlyFailed()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);
        await _repo.AddItemsAsync(new[]
        {
            MakeItem(migration.Id, MigrationItemStatus.Failed, rowNumber: 1),
            MakeItem(migration.Id, MigrationItemStatus.Succeeded, rowNumber: 2),
            MakeItem(migration.Id, MigrationItemStatus.Failed, rowNumber: 3)
        });

        var failed = await _repo.GetFailedItemsAsync(migration.Id);

        Assert.Equal(2, failed.Count);
        Assert.All(failed, i => Assert.Equal(MigrationItemStatus.Failed, i.Status));
    }

    // ── GetItemCountsAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetItemCountsAsync_AggregatesAllBuckets()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);
        await _repo.AddItemsAsync(new[]
        {
            MakeItem(migration.Id, MigrationItemStatus.Pending, staged: true),
            MakeItem(migration.Id, MigrationItemStatus.Pending, staged: false),
            MakeItem(migration.Id, MigrationItemStatus.Processing, staged: true),
            MakeItem(migration.Id, MigrationItemStatus.Succeeded, staged: true),
            MakeItem(migration.Id, MigrationItemStatus.Succeeded, staged: true),
            MakeItem(migration.Id, MigrationItemStatus.Failed, staged: true),
            MakeItem(migration.Id, MigrationItemStatus.Skipped, staged: false)
        });

        var counts = await _repo.GetItemCountsAsync(migration.Id);

        Assert.Equal(7, counts.Total);
        Assert.Equal(2, counts.Pending);
        Assert.Equal(1, counts.Processing);
        Assert.Equal(2, counts.Succeeded);
        Assert.Equal(1, counts.Failed);
        Assert.Equal(1, counts.Skipped);
        Assert.Equal(5, counts.Staged);
        Assert.Equal(1, counts.StagedPending);
    }

    [Fact]
    public async Task GetItemCountsAsync_ForEmptyMigration_ReturnsAllZeros()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);

        var counts = await _repo.GetItemCountsAsync(migration.Id);

        Assert.Equal(0, counts.Total);
        Assert.Equal(0, counts.Staged);
    }

    // ── RemoveAllItemsAsync ──────────────────────────────────────────

    [Fact]
    public async Task RemoveAllItemsAsync_DeletesOnlyForThatMigration()
    {
        var m1 = MakeMigration();
        var m2 = MakeMigration();
        await _repo.CreateAsync(m1);
        await _repo.CreateAsync(m2);
        await _repo.AddItemsAsync(new[] { MakeItem(m1.Id), MakeItem(m1.Id) });
        await _repo.AddItemsAsync(new[] { MakeItem(m2.Id) });

        await _repo.RemoveAllItemsAsync(m1.Id);

        Assert.Equal(0, await _repo.CountItemsAsync(m1.Id, null));
        Assert.Equal(1, await _repo.CountItemsAsync(m2.Id, null));
    }

    // ── MarkItemsStagedAsync ─────────────────────────────────────────

    [Fact]
    public async Task MarkItemsStagedAsync_MatchesByFileNameCaseInsensitively()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);
        await _repo.AddItemsAsync(new[]
        {
            MakeItem(migration.Id, fileName: "Photo1.jpg"),
            MakeItem(migration.Id, fileName: "photo2.jpg"),
            MakeItem(migration.Id, fileName: "other.png")
        });

        var marked = await _repo.MarkItemsStagedAsync(migration.Id, new[] { "photo1.jpg", "PHOTO2.JPG" });

        Assert.Equal(2, marked);
        var items = await _repo.GetItemsAsync(migration.Id, null, 0, 10);
        Assert.Equal(2, items.Count(i => i.IsFileStaged));
    }

    [Fact]
    public async Task MarkItemsStagedAsync_DoesNotRemarkAlreadyStaged()
    {
        var migration = MakeMigration();
        await _repo.CreateAsync(migration);
        await _repo.AddItemsAsync(new[]
        {
            MakeItem(migration.Id, fileName: "a.jpg", staged: true),
            MakeItem(migration.Id, fileName: "b.jpg", staged: false)
        });

        var marked = await _repo.MarkItemsStagedAsync(migration.Id, new[] { "a.jpg", "b.jpg" });

        Assert.Equal(1, marked);
    }

    // ── DeleteByStatusAsync ──────────────────────────────────────────

    [Fact]
    public async Task DeleteByStatusAsync_DeletesMatchingMigrationsAndTheirItems()
    {
        var draft = MakeMigration(status: MigrationStatus.Draft);
        var running = MakeMigration(status: MigrationStatus.Running);
        var completed = MakeMigration(status: MigrationStatus.Completed);
        await _repo.CreateAsync(draft);
        await _repo.CreateAsync(running);
        await _repo.CreateAsync(completed);
        await _repo.AddItemsAsync(new[] { MakeItem(draft.Id), MakeItem(completed.Id) });

        var deleted = await _repo.DeleteByStatusAsync(new[] { MigrationStatus.Draft, MigrationStatus.Completed });

        Assert.Equal(2, deleted);
        Assert.Null(await _repo.GetByIdAsync(draft.Id));
        Assert.Null(await _repo.GetByIdAsync(completed.Id));
        Assert.NotNull(await _repo.GetByIdAsync(running.Id));
    }

    [Fact]
    public async Task DeleteByStatusAsync_NoMatches_ReturnsZero()
    {
        await _repo.CreateAsync(MakeMigration(status: MigrationStatus.Running));

        var deleted = await _repo.DeleteByStatusAsync(new[] { MigrationStatus.Completed });

        Assert.Equal(0, deleted);
    }
}
