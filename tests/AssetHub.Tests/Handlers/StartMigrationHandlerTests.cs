using AssetHub.Application;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Worker.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Handlers;

public class StartMigrationHandlerTests
{
    private readonly Mock<IMigrationRepository> _repo = new();
    private readonly Mock<IAuditService> _audit = new();

    private StartMigrationHandler CreateHandler()
        => new(_repo.Object, _audit.Object, NullLogger<StartMigrationHandler>.Instance);

    private static Migration MakeMigration(
        Guid? id = null,
        MigrationStatus status = MigrationStatus.Running,
        int total = 0)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Test",
            SourceType = MigrationSourceType.CsvUpload,
            Status = status,
            ItemsTotal = total,
            CreatedByUserId = "user-1",
            CreatedAt = DateTime.UtcNow
        };

    private static MigrationItem MakeItem(Guid migrationId, bool staged = true, MigrationItemStatus status = MigrationItemStatus.Pending)
        => new()
        {
            Id = Guid.NewGuid(),
            MigrationId = migrationId,
            FileName = $"file-{Guid.NewGuid():N}.jpg",
            IdempotencyKey = Guid.NewGuid().ToString(),
            Status = status,
            IsFileStaged = staged,
            CreatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task HandleAsync_MigrationNotFound_ReturnsEmpty()
    {
        var cmd = new StartMigrationCommand { MigrationId = Guid.NewGuid() };
        _repo.Setup(r => r.GetByIdAsync(cmd.MigrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Migration?)null);

        var result = await CreateHandler().HandleAsync(cmd, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_MigrationNotRunning_ReturnsEmpty()
    {
        var migration = MakeMigration(status: MigrationStatus.Cancelled);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var result = await CreateHandler().HandleAsync(
            new StartMigrationCommand { MigrationId = migration.Id }, CancellationToken.None);

        Assert.Empty(result);
        _repo.Verify(r => r.GetPendingItemsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NoStagedItems_FinalizesAndAudits()
    {
        var migration = MakeMigration(total: 2);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetPendingItemsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MigrationItem>
            {
                MakeItem(migration.Id, staged: false),
                MakeItem(migration.Id, staged: false)
            });
        _repo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(2, 0, 0, 0, 0, 0, 0, 0));

        var result = await CreateHandler().HandleAsync(
            new StartMigrationCommand { MigrationId = migration.Id }, CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(MigrationStatus.PartiallyCompleted, migration.Status);
        Assert.NotNull(migration.FinishedAt);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.Completed,
            Constants.ScopeTypes.Migration,
            migration.Id,
            null,
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_AllStaged_FansOutItemCommands()
    {
        var migration = MakeMigration(total: 3);
        var items = new List<MigrationItem>
        {
            MakeItem(migration.Id),
            MakeItem(migration.Id),
            MakeItem(migration.Id)
        };
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetPendingItemsAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(items);

        var result = await CreateHandler().HandleAsync(
            new StartMigrationCommand { MigrationId = migration.Id }, CancellationToken.None);

        Assert.Equal(3, result.Length);
        var commands = result.OfType<ProcessMigrationItemCommand>().ToList();
        Assert.Equal(3, commands.Count);
        Assert.All(commands, c => Assert.Equal(migration.Id, c.MigrationId));
        Assert.Contains(commands, c => c.MigrationItemId == items[0].Id);
        Assert.Contains(commands, c => c.MigrationItemId == items[1].Id);
        Assert.Contains(commands, c => c.MigrationItemId == items[2].Id);
    }

    [Fact]
    public async Task HandleAsync_MixedStagedAndUnstaged_OnlyFansOutStaged()
    {
        var migration = MakeMigration(total: 3);
        var staged1 = MakeItem(migration.Id);
        var unstaged = MakeItem(migration.Id, staged: false);
        var staged2 = MakeItem(migration.Id);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetPendingItemsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MigrationItem> { staged1, unstaged, staged2 });

        var result = await CreateHandler().HandleAsync(
            new StartMigrationCommand { MigrationId = migration.Id }, CancellationToken.None);

        Assert.Equal(2, result.Length);
        var ids = result.OfType<ProcessMigrationItemCommand>().Select(c => c.MigrationItemId).ToHashSet();
        Assert.Contains(staged1.Id, ids);
        Assert.Contains(staged2.Id, ids);
        Assert.DoesNotContain(unstaged.Id, ids);
    }

    [Fact]
    public async Task HandleAsync_FinalizeWithFailures_SetsCompletedWithErrors()
    {
        var migration = MakeMigration(total: 2);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetPendingItemsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MigrationItem>());
        _repo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(2, 0, 0, 1, 1, 0, 2, 0));

        await CreateHandler().HandleAsync(
            new StartMigrationCommand { MigrationId = migration.Id }, CancellationToken.None);

        Assert.Equal(MigrationStatus.CompletedWithErrors, migration.Status);
        Assert.Equal(1, migration.ItemsSucceeded);
        Assert.Equal(1, migration.ItemsFailed);
    }

    [Fact]
    public async Task HandleAsync_FinalizeAllSucceeded_SetsCompleted()
    {
        var migration = MakeMigration(total: 2);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetPendingItemsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MigrationItem>());
        _repo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(2, 0, 0, 2, 0, 0, 2, 0));

        await CreateHandler().HandleAsync(
            new StartMigrationCommand { MigrationId = migration.Id }, CancellationToken.None);

        Assert.Equal(MigrationStatus.Completed, migration.Status);
    }
}
