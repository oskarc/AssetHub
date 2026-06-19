using AssetHub.Ui.Components.Admin;
using AssetHub.Ui.Tests.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Ui.Tests.Components;

// AdminBreadcrumbs auto-derives the "Admin > {section}" trail from the URL.
// The stub localizer returns the resource KEY as the value, so assertions match
// on the keys the component resolves (Nav_Admin, Tab_*, PageTitle, Verify_PageTitle).
public class AdminBreadcrumbsTests : BunitTestBase
{
    private void NavigateTo(string relativePath) =>
        Services.GetRequiredService<NavigationManager>().NavigateTo(relativePath);

    [Fact]
    public void Renders_AdminRoot_Link_And_Section_Leaf_For_Subpage()
    {
        NavigateTo("admin/users");

        var cut = Render<AdminBreadcrumbs>();

        Assert.Contains("Nav_Admin", cut.Markup);          // root crumb (links to /admin)
        Assert.Contains("Tab_UserManagement", cut.Markup); // current-section leaf
        Assert.Contains("href=\"/admin\"", cut.Markup);    // root crumb is a link
    }

    [Fact]
    public void Root_Admin_Page_Renders_No_Breadcrumb()
    {
        NavigateTo("admin");

        var cut = Render<AdminBreadcrumbs>();

        // A single-item trail (just "Admin") is the section root — suppressed.
        Assert.DoesNotContain("mud-breadcrumbs", cut.Markup);
    }

    [Theory]
    [InlineData("admin/migrations", "Tab_Migrations")]
    [InlineData("admin/audit", "Tab_AuditLog")]
    [InlineData("admin/analytics", "PageTitle")]              // AnalyticsResource
    [InlineData("admin/watermarks/verify", "Verify_PageTitle")] // WatermarksResource, 2 levels deep
    public void Derives_Correct_Section_Leaf(string path, string expectedKey)
    {
        NavigateTo(path);

        var cut = Render<AdminBreadcrumbs>();

        Assert.Contains(expectedKey, cut.Markup);
    }

    [Fact]
    public void Updates_Trail_On_Navigation()
    {
        NavigateTo("admin/users");
        var cut = Render<AdminBreadcrumbs>();
        Assert.Contains("Tab_UserManagement", cut.Markup);

        NavigateTo("admin/webhooks");

        Assert.Contains("Tab_Webhooks", cut.Markup);
        Assert.DoesNotContain("Tab_UserManagement", cut.Markup);
    }
}
