using Botty.Core.Enums;
using Botty.Core.Services;
using Xunit;

namespace Botty.Tools.Tests;

public class ToolApprovalPolicyTests
{
    [Theory]
    [InlineData("gmail_send_message", TaskType.SendMessage)]
    [InlineData("channel_send_message", TaskType.SendMessage)]
    [InlineData("shell_execute", TaskType.ShellCommand)]
    [InlineData("calendar_create_event", TaskType.CalendarChange)]
    [InlineData("script_delete", TaskType.SystemChange)]
    public void TryGetApprovalTaskType_ReturnsExpectedMapping(string toolName, TaskType expectedType)
    {
        var requiresApproval = ToolApprovalPolicy.TryGetApprovalTaskType(toolName, out var taskType);

        Assert.True(requiresApproval);
        Assert.Equal(expectedType, taskType);
    }

    [Theory]
    [InlineData("gmail_list_messages")]
    [InlineData("calendar_list_events")]
    [InlineData("url_fetch")]
    [InlineData("brave_web_search")]
    public void TryGetApprovalTaskType_DoesNotRequireApproval_ForReadOnlyTools(string toolName)
    {
        var requiresApproval = ToolApprovalPolicy.TryGetApprovalTaskType(toolName, out _);

        Assert.False(requiresApproval);
    }
}
