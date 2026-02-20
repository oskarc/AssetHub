using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the EmptyState component.
/// Verifies rendering of title, description, icon, action button, and child content.
/// </summary>
public class EmptyStateTests : BunitTestBase
{
    [Fact]
    public void Renders_Title()
    {
        var cut = Render<EmptyState>(p => p
            .Add(x => x.Title, "No items found"));

        cut.Find(".mud-typography-h6").TextContent.MarkupMatches("No items found");
    }

    [Fact]
    public void Renders_Description_When_Provided()
    {
        var cut = Render<EmptyState>(p => p
            .Add(x => x.Title, "Empty")
            .Add(x => x.Description, "Try adding some items"));

        cut.Find(".mud-typography-body2").TextContent.MarkupMatches("Try adding some items");
    }

    [Fact]
    public void Hides_Description_When_Null()
    {
        var cut = Render<EmptyState>(p => p
            .Add(x => x.Title, "Empty")
            .Add(x => x.Description, (string?)null));

        Assert.Empty(cut.FindAll(".mud-typography-body2"));
    }

    [Fact]
    public void Renders_Icon()
    {
        var cut = Render<EmptyState>(p => p
            .Add(x => x.Title, "Empty")
            .Add(x => x.Icon, Icons.Material.Filled.Inbox));

        Assert.NotNull(cut.Find(".mud-icon-root"));
    }

    [Fact]
    public void Renders_ActionButton_When_ActionText_And_Callback_Provided()
    {
        var clicked = false;
        var cut = Render<EmptyState>(p => p
            .Add(x => x.Title, "Empty")
            .Add(x => x.ActionText, "Add Item")
            .Add(x => x.OnAction, () => { clicked = true; }));

        var button = cut.Find(".mud-button-filled");
        Assert.Contains("Add Item", button.TextContent);

        button.Click();
        Assert.True(clicked);
    }

    [Fact]
    public void Hides_ActionButton_When_No_ActionText()
    {
        var cut = Render<EmptyState>(p => p
            .Add(x => x.Title, "Empty"));

        Assert.Empty(cut.FindAll(".mud-button-filled"));
    }

    [Fact]
    public void Renders_ChildContent()
    {
        var cut = Render<EmptyState>(p => p
            .Add(x => x.Title, "Empty")
            .Add(x => x.ChildContent, "<p class='custom-child'>Hello</p>"));

        Assert.NotNull(cut.Find(".custom-child"));
    }

    [Fact]
    public void Applies_Custom_CssClass()
    {
        var cut = Render<EmptyState>(p => p
            .Add(x => x.Title, "Empty")
            .Add(x => x.Class, "my-custom-class"));

        Assert.Contains("my-custom-class", cut.Markup);
    }

    [Fact]
    public void Default_Title_Is_NothingHereYet()
    {
        var cut = Render<EmptyState>();

        cut.Find(".mud-typography-h6").TextContent.MarkupMatches("Nothing here yet");
    }
}
