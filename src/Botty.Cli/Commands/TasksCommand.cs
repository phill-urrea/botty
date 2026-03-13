using System.CommandLine;
using Botty.Cli.Infrastructure;
using Botty.Cli.Models;
using AppContext = Botty.Cli.Infrastructure.AppContext;
using Fmt = Botty.Cli.Infrastructure.OutputFormatter;

namespace Botty.Cli.Commands;

public static class TasksCommand
{
    public static Command Create()
    {
        var command = new Command("tasks", "Manage Kanban tasks");
        command.AddCommand(CreateList());
        command.AddCommand(CreateShow());
        command.AddCommand(CreateNew());
        command.AddCommand(CreateApprove());
        command.AddCommand(CreateReject());
        command.AddCommand(CreateMove());
        return command;
    }

    private static Command CreateList()
    {
        var laneOption = new Option<string?>("--lane", "Filter by lane");
        laneOption.AddCompletions("ToDo", "InProgress", "NeedsApproval", "Done", "Cancelled");
        var assigneeOption = new Option<string?>("--assignee", "Filter by assignee");
        assigneeOption.AddCompletions("User", "Assistant");

        var cmd = new Command("list", "List tasks");
        cmd.AddOption(laneOption);
        cmd.AddOption(assigneeOption);
        cmd.SetHandler(async (string? lane, string? assignee) =>
        {
            List<KanbanTaskDto> tasks;

            if (lane is not null)
            {
                tasks = await AppContext.Client.GetAsync<List<KanbanTaskDto>>($"api/kanban/lane/{lane}");
            }
            else if (assignee is not null)
            {
                tasks = await AppContext.Client.GetAsync<List<KanbanTaskDto>>($"api/kanban/assignee/{assignee}");
            }
            else
            {
                var board = await AppContext.Client.GetAsync<KanbanBoardDto>("api/kanban/board");
                tasks = [.. board.ToDo, .. board.InProgress, .. board.NeedsApproval, .. board.Done, .. board.Cancelled];
            }

            Fmt.RenderList(tasks,
                ["Id", "Title", "Lane", "Assignee", "Priority", "Created"],
                task => [
                    Fmt.ShortGuid(task.Id),
                    Truncate(task.Title, 40),
                    task.Lane,
                    task.Assignee,
                    task.Priority,
                    Fmt.FormatDate(task.CreatedAt)
                ]);
        }, laneOption, assigneeOption);
        return cmd;
    }

    private static Command CreateShow()
    {
        var idArg = new Argument<Guid>("id", "Task ID");
        var cmd = new Command("show", "Show task details") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            var task = await AppContext.Client.GetAsync<KanbanTaskDto>($"api/kanban/{id}");
            if (AppContext.OutputFormat != "table")
            {
                Fmt.Render(task);
                return;
            }
            var rows = new List<(string, string)>
            {
                ("Id", task.Id.ToString()),
                ("Title", task.Title),
                ("Description", task.Description ?? "-"),
                ("Lane", task.Lane),
                ("Assignee", task.Assignee),
                ("Type", task.Type),
                ("Priority", task.Priority),
                ("Created", Fmt.FormatDate(task.CreatedAt)),
                ("Updated", Fmt.FormatDate(task.UpdatedAt)),
            };
            if (task.ApprovedAt.HasValue)
                rows.Add(("Approved", Fmt.FormatDate(task.ApprovedAt)));
            if (task.CompletedAt.HasValue)
                rows.Add(("Completed", Fmt.FormatDate(task.CompletedAt)));
            if (task.HasPendingAction)
            {
                rows.Add(("Pending Action", task.PendingActionType ?? "-"));
                rows.Add(("Action Detail", task.PendingActionDescription ?? "-"));
            }
            if (task.RejectionReason is not null)
                rows.Add(("Rejection Reason", task.RejectionReason));
            if (task.ExecutionResult is not null)
                rows.Add(("Execution Result", Truncate(task.ExecutionResult, 200)));
            Fmt.RenderDetail([.. rows]);
        }, idArg);
        return cmd;
    }

    private static Command CreateNew()
    {
        var titleOption = new Option<string>("--title", "Task title") { IsRequired = true };
        var descOption = new Option<string?>("--description", "Task description");
        var laneOption = new Option<string?>("--lane", "Lane (default: ToDo)");
        laneOption.AddCompletions("ToDo", "InProgress", "NeedsApproval");
        var assigneeOption = new Option<string?>("--assignee", "Assignee (default: User)");
        assigneeOption.AddCompletions("User", "Assistant");
        var typeOption = new Option<string?>("--type", "Task type (default: General)");
        typeOption.AddCompletions("General", "SendMessage", "SystemChange", "SkillExecution");
        var priorityOption = new Option<string?>("--priority", "Priority (default: Normal)");
        priorityOption.AddCompletions("Low", "Normal", "High", "Urgent");

        var cmd = new Command("create", "Create a new task");
        cmd.AddOption(titleOption);
        cmd.AddOption(descOption);
        cmd.AddOption(laneOption);
        cmd.AddOption(assigneeOption);
        cmd.AddOption(typeOption);
        cmd.AddOption(priorityOption);
        cmd.SetHandler(async (string title, string? description, string? lane, string? assignee, string? type, string? priority) =>
        {
            var body = new Dictionary<string, object?> { ["title"] = title };
            if (description is not null) body["description"] = description;
            if (lane is not null) body["lane"] = lane;
            if (assignee is not null) body["assignee"] = assignee;
            if (type is not null) body["type"] = type;
            if (priority is not null) body["priority"] = priority;

            var task = await AppContext.Client.PostAsync<KanbanTaskDto>("api/kanban", body);
            Fmt.Success($"Task created: {Fmt.ShortGuid(task.Id)} - {task.Title}");
        }, titleOption, descOption, laneOption, assigneeOption, typeOption, priorityOption);
        return cmd;
    }

    private static Command CreateApprove()
    {
        var idArg = new Argument<Guid>("id", "Task ID");
        var cmd = new Command("approve", "Approve a task") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            var task = await AppContext.Client.PostAsync<KanbanTaskDto>($"api/kanban/{id}/approve");
            Fmt.Success($"Task '{task.Title}' approved");
        }, idArg);
        return cmd;
    }

    private static Command CreateReject()
    {
        var idArg = new Argument<Guid>("id", "Task ID");
        var reasonOption = new Option<string>("--reason", "Rejection reason") { IsRequired = true };
        var cmd = new Command("reject", "Reject a task") { idArg };
        cmd.AddOption(reasonOption);
        cmd.SetHandler(async (Guid id, string reason) =>
        {
            var task = await AppContext.Client.PostAsync<KanbanTaskDto>(
                $"api/kanban/{id}/reject", new { reason });
            Fmt.Success($"Task '{task.Title}' rejected");
        }, idArg, reasonOption);
        return cmd;
    }

    private static Command CreateMove()
    {
        var idArg = new Argument<Guid>("id", "Task ID");
        var laneOption = new Option<string>("--lane", "Target lane") { IsRequired = true };
        laneOption.AddCompletions("ToDo", "InProgress", "NeedsApproval", "Done", "Cancelled");
        var cmd = new Command("move", "Move task to a different lane") { idArg };
        cmd.AddOption(laneOption);
        cmd.SetHandler(async (Guid id, string lane) =>
        {
            var task = await AppContext.Client.PostAsync<KanbanTaskDto>(
                $"api/kanban/{id}/move", new { lane });
            Fmt.Success($"Task '{task.Title}' moved to {task.Lane}");
        }, idArg, laneOption);
        return cmd;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";
}
