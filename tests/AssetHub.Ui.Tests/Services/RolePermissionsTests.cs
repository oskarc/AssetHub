namespace AssetHub.Ui.Tests.Services;

/// <summary>
/// Tests for the RolePermissions static utility class.
/// Verifies role-based permission checks for all four roles.
/// </summary>
public class RolePermissionsTests
{
    // ===== CanUpload (contributor+) =====

    [Theory]
    [InlineData("viewer", false)]
    [InlineData("contributor", true)]
    [InlineData("manager", true)]
    [InlineData("admin", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("unknown", false)]
    public void CanUpload_Returns_Correct_Result(string? role, bool expected)
    {
        Assert.Equal(expected, RolePermissions.CanUpload(role));
    }

    // ===== CanShare (contributor+) =====

    [Theory]
    [InlineData("viewer", false)]
    [InlineData("contributor", true)]
    [InlineData("manager", true)]
    [InlineData("admin", true)]
    [InlineData(null, false)]
    public void CanShare_Returns_Correct_Result(string? role, bool expected)
    {
        Assert.Equal(expected, RolePermissions.CanShare(role));
    }

    // ===== CanEdit (contributor+) =====

    [Theory]
    [InlineData("viewer", false)]
    [InlineData("contributor", true)]
    [InlineData("manager", true)]
    [InlineData("admin", true)]
    [InlineData(null, false)]
    public void CanEdit_Returns_Correct_Result(string? role, bool expected)
    {
        Assert.Equal(expected, RolePermissions.CanEdit(role));
    }

    // ===== CanManageCollections (contributor+) =====

    [Theory]
    [InlineData("viewer", false)]
    [InlineData("contributor", true)]
    [InlineData("manager", true)]
    [InlineData("admin", true)]
    public void CanManageCollections_Returns_Correct_Result(string? role, bool expected)
    {
        Assert.Equal(expected, RolePermissions.CanManageCollections(role));
    }

    // ===== CanDelete (manager+) =====

    [Theory]
    [InlineData("viewer", false)]
    [InlineData("contributor", false)]
    [InlineData("manager", true)]
    [InlineData("admin", true)]
    [InlineData(null, false)]
    public void CanDelete_Returns_Correct_Result(string? role, bool expected)
    {
        Assert.Equal(expected, RolePermissions.CanDelete(role));
    }

    // ===== CanManageAccess (manager+) =====

    [Theory]
    [InlineData("viewer", false)]
    [InlineData("contributor", false)]
    [InlineData("manager", true)]
    [InlineData("admin", true)]
    [InlineData(null, false)]
    public void CanManageAccess_Returns_Correct_Result(string? role, bool expected)
    {
        Assert.Equal(expected, RolePermissions.CanManageAccess(role));
    }

    // ===== Case insensitivity =====

    [Theory]
    [InlineData("Viewer")]
    [InlineData("VIEWER")]
    [InlineData("vIeWeR")]
    public void Roles_Are_Case_Insensitive(string role)
    {
        // Should behave the same as lowercase
        Assert.False(RolePermissions.CanUpload(role));
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("Admin")]
    [InlineData("aDmIn")]
    public void Admin_CaseInsensitive_Has_All_Permissions(string role)
    {
        Assert.True(RolePermissions.CanUpload(role));
        Assert.True(RolePermissions.CanShare(role));
        Assert.True(RolePermissions.CanEdit(role));
        Assert.True(RolePermissions.CanDelete(role));
        Assert.True(RolePermissions.CanManageAccess(role));
        Assert.True(RolePermissions.CanManageCollections(role));
    }
}
