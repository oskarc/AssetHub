namespace AssetHub.Application.Dtos;

/// <summary>
/// Aggregated dashboard data, scoped by the requesting user's role.
/// </summary>
public class DashboardDto
{
    /// <summary>Role of the requesting user (admin, manager, contributor, viewer).</summary>
    public string UserRole { get; set; } = string.Empty;

    /// <summary>Most recent assets the user has access to.</summary>
    public List<AssetResponseDto> RecentAssets { get; set; } = [];

    /// <summary>Collections the user has access to (root-level).</summary>
    public List<CollectionResponseDto> Collections { get; set; } = [];

    /// <summary>Active shares visible to the user (admin: all, manager: own).</summary>
    public List<DashboardShareDto> RecentShares { get; set; } = [];

    /// <summary>Recent audit events visible to the user (admin: all, manager: own).</summary>
    public List<AuditEventDto> RecentActivity { get; set; } = [];

    /// <summary>Global stats — only populated for admins.</summary>
    public DashboardStatsDto? Stats { get; set; }
}

/// <summary>
/// Platform-wide statistics, only visible to admins.
/// </summary>
public class DashboardStatsDto
{
    public int TotalAssets { get; set; }
    public long TotalStorageBytes { get; set; }
    public int TotalCollections { get; set; }
    public int TotalUsers { get; set; }
    public int ViewerCount { get; set; }
    public int ContributorCount { get; set; }
    public int ManagerCount { get; set; }
    public int AdminCount { get; set; }
    public int UnassignedCount { get; set; }
    public int ActiveShares { get; set; }
    public int ExpiredShares { get; set; }
    public int RevokedShares { get; set; }
    public int TotalShares { get; set; }
    public int TotalAuditEvents { get; set; }
    public List<StorageByTypeDto> StorageByType { get; set; } = [];
}

public class StorageByTypeDto
{
    public string AssetType { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Lightweight share summary for the dashboard.
/// </summary>
public class DashboardShareDto
{
    public Guid Id { get; set; }
    public string ScopeType { get; set; } = string.Empty;
    public Guid ScopeId { get; set; }
    public string? ScopeName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int AccessCount { get; set; }
    public bool HasPassword { get; set; }
    public string Status { get; set; } = string.Empty;
}
