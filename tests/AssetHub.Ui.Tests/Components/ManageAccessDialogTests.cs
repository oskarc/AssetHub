using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the ManageAccessDialog component.
/// Verifies rendering of ACL list, user search, grant/revoke flow, and role guards.
/// </summary>
public class ManageAccessDialogTests : BunitTestBase
{
    private readonly Guid _collectionId = Guid.NewGuid();

    private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync(
        string currentUserRole = "manager",
        List<CollectionAclResponseDto>? aclEntries = null)
    {
        var entries = aclEntries ?? new List<CollectionAclResponseDto>
        {
            TestData.CreateAclEntry(principalId: "user-1", principalName: "alice", role: "viewer"),
            TestData.CreateAclEntry(principalId: "user-2", principalName: "bob", role: "contributor")
        };

        MockApi.Setup(a => a.GetCollectionAclsAsync(_collectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        var parameters = new DialogParameters<ManageAccessDialog>
        {
            { x => x.CollectionId, _collectionId },
            { x => x.CollectionName, "Test Collection" },
            { x => x.CurrentUserRole, currentUserRole }
        };
        return await ShowDialogAsync<ManageAccessDialog>(parameters);
    }

    [Fact]
    public async Task Renders_Dialog_Title()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("ManageAccess", cut.Markup);
    }

    [Fact]
    public async Task Shows_Collection_Name()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Test Collection", cut.Markup);
    }

    [Fact]
    public async Task Shows_Current_User_Role()
    {
        var cut = await RenderDialogAsync(currentUserRole: "manager");

        Assert.Contains("manager", cut.Markup);
    }

    [Fact]
    public async Task Renders_ACL_Entries()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("alice", cut.Markup);
        Assert.Contains("bob", cut.Markup);
    }

    [Fact]
    public async Task Shows_Role_Chips_For_Entries()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Role_Viewer", cut.Markup);
        Assert.Contains("Role_Contributor", cut.Markup);
    }

    [Fact]
    public async Task Shows_Entry_Count()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("CurrentAccess", cut.Markup);
        Assert.Contains("2", cut.Markup); // 2 entries
    }

    [Fact]
    public async Task Shows_EmptyState_When_No_ACLs()
    {
        var cut = await RenderDialogAsync(aclEntries: []);

        Assert.Contains("NoAccessEntries", cut.Markup);
    }

    [Fact]
    public async Task Has_User_Search_Section()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("AddUserAccess", cut.Markup);
        Assert.Contains("SearchUser", cut.Markup);
    }

    [Fact]
    public async Task Has_Role_Selector()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Label_Role", cut.Markup);
    }

    [Fact]
    public async Task Has_Add_Button()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_Add", cut.Markup);
        Assert.Contains(Icons.Material.Filled.PersonAdd, cut.Markup);
    }

    [Fact]
    public async Task Has_Close_Button()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_Close", cut.Markup);
    }

    [Fact]
    public async Task Manager_Can_Edit_Viewer_Entry()
    {
        var entries = new List<CollectionAclResponseDto>
        {
            TestData.CreateAclEntry(role: "viewer", principalName: "testuser")
        };

        var cut = await RenderDialogAsync(currentUserRole: "manager", aclEntries: entries);

        // Should show edit and revoke buttons for viewer role
        Assert.Contains(Icons.Material.Filled.Edit, cut.Markup);
        Assert.Contains(Icons.Material.Filled.PersonRemove, cut.Markup);
    }

    [Fact]
    public async Task Manager_Cannot_Edit_Admin_Entry()
    {
        var entries = new List<CollectionAclResponseDto>
        {
            TestData.CreateAclEntry(role: "admin", principalName: "adminuser")
        };

        var cut = await RenderDialogAsync(currentUserRole: "manager", aclEntries: entries);

        // Should show "InsufficientLevel" instead of edit/revoke
        Assert.Contains("InsufficientLevel", cut.Markup);
    }

    [Fact]
    public async Task Manager_Can_See_Viewer_And_Contributor_Role_Options()
    {
        var cut = await RenderDialogAsync(currentUserRole: "manager");

        // Open the role MudSelect dropdown (MudBlazor 8 uses mousedown)
        var selects = cut.FindAll("div.mud-select div.mud-input-control");
        await selects[^1].MouseDownAsync();

        // MudSelectItem contents render inside MudPopoverProvider
        Assert.Contains("Role_Viewer", PopoverProvider!.Markup);
        Assert.Contains("Role_Contributor", PopoverProvider!.Markup);
        Assert.Contains("Role_Manager", PopoverProvider!.Markup);
    }

    [Fact]
    public async Task Loads_ACLs_On_Init()
    {
        await RenderDialogAsync();

        MockApi.Verify(a => a.GetCollectionAclsAsync(_collectionId, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handles_ACL_Load_Error()
    {
        MockApi.Setup(a => a.GetCollectionAclsAsync(_collectionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Failed"));

        var parameters = new DialogParameters<ManageAccessDialog>
        {
            { x => x.CollectionId, _collectionId },
            { x => x.CollectionName, "Test" },
            { x => x.CurrentUserRole, "manager" }
        };
        await ShowDialogAsync<ManageAccessDialog>(parameters);

        VerifyHandleErrorCalled();
    }

    [Fact]
    public async Task Shows_Security_Icon()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains(Icons.Material.Filled.Security, cut.Markup);
    }

    // ── Revoke access submission flow ───────────────────────────────

    [Fact]
    public async Task RevokeAccess_Calls_Api_And_Shows_Success()
    {
        var entry = TestData.CreateAclEntry(principalId: "user-revoke", principalName: "revoke-me", role: "viewer");
        var entries = new List<CollectionAclResponseDto> { entry };

        MockApi.Setup(a => a.GetCollectionAclsAsync(_collectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        MockApi.Setup(a => a.RevokeCollectionAccessAsync(
                _collectionId, "user", "user-revoke", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cut = await RenderDialogAsync(currentUserRole: "manager", aclEntries: entries);

        // Find the revoke button by its error color CSS class (Color.Error on MudIconButton)
        var revokeBtn = cut.Find("button.mud-error-text");
        Assert.NotNull(revokeBtn);

        await cut.InvokeAsync(() => revokeBtn.Click());

        // Confirm the MudMessageBox by clicking the yes button ("Btn_Revoke" via StubStringLocalizer)
        var confirmBtn = cut.FindAll("button")
            .FirstOrDefault(b => b.TextContent.Contains("Btn_Revoke"));
        Assert.NotNull(confirmBtn);
        await cut.InvokeAsync(() => confirmBtn!.Click());

        MockApi.Verify(a => a.RevokeCollectionAccessAsync(
            _collectionId, "user", "user-revoke", It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task RevokeAccess_Error_Calls_HandleError()
    {
        var entry = TestData.CreateAclEntry(principalId: "user-err", principalName: "error-user", role: "viewer");
        var entries = new List<CollectionAclResponseDto> { entry };

        MockApi.Setup(a => a.GetCollectionAclsAsync(_collectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        MockApi.Setup(a => a.RevokeCollectionAccessAsync(
                _collectionId, "user", "user-err", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        var cut = await RenderDialogAsync(currentUserRole: "manager", aclEntries: entries);

        var revokeBtn = cut.Find("button.mud-error-text");
        Assert.NotNull(revokeBtn);

        await cut.InvokeAsync(() => revokeBtn.Click());

        // Confirm the MudMessageBox by clicking the yes button ("Btn_Revoke" via StubStringLocalizer)
        var confirmBtn = cut.FindAll("button")
            .FirstOrDefault(b => b.TextContent.Contains("Btn_Revoke"));
        Assert.NotNull(confirmBtn);
        await cut.InvokeAsync(() => confirmBtn!.Click());

        VerifyHandleErrorCalled();
    }
}
