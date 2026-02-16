using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Skills.Browser;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Botty.Skills.Tests;

public class BrowserSkillTests
{
    // --- AccessibilitySnapshotBuilder.Build tests ---

    [Fact]
    public void Build_AssignsRefsToInteractiveElements()
    {
        var yaml = """
            - navigation "Main":
              - link "Home"
              - link "About"
            - main:
              - heading "Welcome"
              - button "Sign In"
              - textbox "Email"
            """;

        var (snapshot, refs, truncated) = AccessibilitySnapshotBuilder.Build(yaml, interactiveOnly: false);

        Assert.False(truncated);
        Assert.Contains("[ref=e1]", snapshot);
        Assert.Contains("[ref=e2]", snapshot);
        Assert.Contains("[ref=e3]", snapshot); // heading
        Assert.Contains("[ref=e4]", snapshot); // button
        Assert.Contains("[ref=e5]", snapshot); // textbox

        Assert.Equal("link", refs["e1"].Role);
        Assert.Equal("Home", refs["e1"].Name);
        Assert.Equal("link", refs["e2"].Role);
        Assert.Equal("About", refs["e2"].Name);
        Assert.Equal("heading", refs["e3"].Role);
        Assert.Equal("button", refs["e4"].Role);
        Assert.Equal("textbox", refs["e5"].Role);
    }

    [Fact]
    public void Build_InteractiveOnlyFiltersNonInteractiveElements()
    {
        var yaml = """
            - heading "Title"
            - paragraph "Some text"
            - button "Click Me"
            - link "Go"
            - textbox "Name"
            """;

        var (snapshot, refs, _) = AccessibilitySnapshotBuilder.Build(yaml, interactiveOnly: true);

        Assert.Equal(3, refs.Count);
        Assert.DoesNotContain("heading", snapshot);
        Assert.DoesNotContain("paragraph", snapshot);
        Assert.Contains("button", snapshot);
        Assert.Contains("link", snapshot);
        Assert.Contains("textbox", snapshot);
    }

    [Fact]
    public void Build_TracksNthForDuplicateRoleAndName()
    {
        var yaml = """
            - button "Save"
            - button "Save"
            - button "Cancel"
            """;

        var (_, refs, _) = AccessibilitySnapshotBuilder.Build(yaml, interactiveOnly: false);

        Assert.Equal(3, refs.Count);
        // First "Save" button: nth=0
        Assert.Equal(0, refs["e1"].Nth);
        Assert.Equal("Save", refs["e1"].Name);
        // Second "Save" button: nth=1
        Assert.Equal(1, refs["e2"].Nth);
        Assert.Equal("Save", refs["e2"].Name);
        // "Cancel" button: nth=0
        Assert.Equal(0, refs["e3"].Nth);
        Assert.Equal("Cancel", refs["e3"].Name);
    }

    [Fact]
    public void Build_TruncatesAtMaxChars()
    {
        var yaml = string.Join("\n", Enumerable.Range(0, 1000)
            .Select(i => $"- button \"Button {i}\""));

        var (snapshot, _, truncated) = AccessibilitySnapshotBuilder.Build(yaml, interactiveOnly: false, maxChars: 500);

        Assert.True(truncated);
        Assert.Contains("... (truncated)", snapshot);
        Assert.True(snapshot.Length <= 600); // Some margin for the truncation message
    }

    [Fact]
    public void Build_EmptyInput_ReturnsEmptyPage()
    {
        var (snapshot, refs, truncated) = AccessibilitySnapshotBuilder.Build("", interactiveOnly: false);

        Assert.Equal("(empty page)", snapshot);
        Assert.Empty(refs);
        Assert.False(truncated);
    }

    [Fact]
    public void Build_WhitespaceInput_ReturnsEmptyPage()
    {
        var (snapshot, refs, truncated) = AccessibilitySnapshotBuilder.Build("   \n  ", interactiveOnly: false);

        Assert.Equal("(empty page)", snapshot);
        Assert.Empty(refs);
        Assert.False(truncated);
    }

    [Fact]
    public void Build_NamedContentGetsRefs()
    {
        var yaml = """
            - heading "Page Title"
            - cell "Data"
            - paragraph "Not named content role"
            """;

        var (snapshot, refs, _) = AccessibilitySnapshotBuilder.Build(yaml, interactiveOnly: false);

        Assert.Equal(2, refs.Count); // heading + cell
        Assert.Equal("heading", refs["e1"].Role);
        Assert.Equal("cell", refs["e2"].Role);
    }

    [Fact]
    public void IsInteractiveRole_ReturnsTrueForInteractiveRoles()
    {
        Assert.True(AccessibilitySnapshotBuilder.IsInteractiveRole("button"));
        Assert.True(AccessibilitySnapshotBuilder.IsInteractiveRole("link"));
        Assert.True(AccessibilitySnapshotBuilder.IsInteractiveRole("textbox"));
        Assert.True(AccessibilitySnapshotBuilder.IsInteractiveRole("checkbox"));
        Assert.True(AccessibilitySnapshotBuilder.IsInteractiveRole("combobox"));
    }

    [Fact]
    public void IsInteractiveRole_ReturnsFalseForNonInteractiveRoles()
    {
        Assert.False(AccessibilitySnapshotBuilder.IsInteractiveRole("heading"));
        Assert.False(AccessibilitySnapshotBuilder.IsInteractiveRole("paragraph"));
        Assert.False(AccessibilitySnapshotBuilder.IsInteractiveRole("main"));
        Assert.False(AccessibilitySnapshotBuilder.IsInteractiveRole("navigation"));
    }

    // --- BrowserSkill.GetTools tests ---

    [Fact]
    public void GetTools_ReturnsSixTools()
    {
        var skill = CreateSkill();
        var tools = skill.GetTools().ToList();

        Assert.Equal(6, tools.Count);
        Assert.Contains(tools, t => t.Name == "browser_navigate");
        Assert.Contains(tools, t => t.Name == "browser_snapshot");
        Assert.Contains(tools, t => t.Name == "browser_click");
        Assert.Contains(tools, t => t.Name == "browser_type");
        Assert.Contains(tools, t => t.Name == "browser_screenshot");
        Assert.Contains(tools, t => t.Name == "browser_evaluate");
    }

    // --- BrowserSkill.OnExecuteAsync error handling tests ---

    [Fact]
    public async Task Execute_UnknownTool_Fails()
    {
        var skill = CreateSkill();
        await InitializeAsync(skill);

        var result = await skill.ExecuteAsync(new SkillContext
        {
            ToolName = "browser_unknown",
            Arguments = "{}"
        });

        Assert.False(result.Success);
        Assert.Contains("Unknown tool", result.Error ?? string.Empty);
    }

    [Fact]
    public async Task Navigate_MissingUrl_Fails()
    {
        var skill = CreateSkill();
        await InitializeAsync(skill);

        var result = await skill.ExecuteAsync(new SkillContext
        {
            ToolName = "browser_navigate",
            Arguments = "{}"
        });

        Assert.False(result.Success);
        Assert.Contains("url", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Click_MissingRef_Fails()
    {
        var skill = CreateSkill();
        await InitializeAsync(skill);

        var result = await skill.ExecuteAsync(new SkillContext
        {
            ToolName = "browser_click",
            Arguments = "{}"
        });

        Assert.False(result.Success);
        Assert.Contains("ref", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Type_MissingRef_Fails()
    {
        var skill = CreateSkill();
        await InitializeAsync(skill);

        var result = await skill.ExecuteAsync(new SkillContext
        {
            ToolName = "browser_type",
            Arguments = """{"text": "hello"}"""
        });

        Assert.False(result.Success);
        Assert.Contains("ref", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Type_MissingText_Fails()
    {
        var skill = CreateSkill();
        await InitializeAsync(skill);

        var result = await skill.ExecuteAsync(new SkillContext
        {
            ToolName = "browser_type",
            Arguments = """{"ref": "e1"}"""
        });

        Assert.False(result.Success);
        Assert.Contains("text", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluate_MissingExpression_Fails()
    {
        var skill = CreateSkill();
        await InitializeAsync(skill);

        var result = await skill.ExecuteAsync(new SkillContext
        {
            ToolName = "browser_evaluate",
            Arguments = "{}"
        });

        Assert.False(result.Success);
        Assert.Contains("expression", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static BrowserSkill CreateSkill()
    {
        var session = new BrowserSession(NullLogger<BrowserSession>.Instance);
        return new BrowserSkill(session, NullLogger<BrowserSkill>.Instance);
    }

    private static async Task InitializeAsync(BrowserSkill skill)
    {
        await skill.InitializeAsync(new SkillConfiguration
        {
            SkillId = "browser",
            Values = new Dictionary<string, string?>
            {
                ["headless"] = "true",
                ["default_timeout_seconds"] = "30"
            }
        });
    }
}
