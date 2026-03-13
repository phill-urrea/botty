using System.CommandLine;
using Botty.Cli.Infrastructure;
using Botty.Cli.Models;
using AppContext = Botty.Cli.Infrastructure.AppContext;
using Fmt = Botty.Cli.Infrastructure.OutputFormatter;

namespace Botty.Cli.Commands;

public static class ScheduleCommand
{
    public static Command Create()
    {
        var command = new Command("schedule", "Manage scheduled tasks");
        command.AddCommand(CreateList());
        command.AddCommand(CreateShow());
        command.AddCommand(CreateNew());
        command.AddCommand(CreatePause());
        command.AddCommand(CreateResume());
        command.AddCommand(CreateTrigger());
        command.AddCommand(CreateDelete());
        return command;
    }

    private static Command CreateList()
    {
        var cmd = new Command("list", "List scheduled tasks");
        cmd.SetHandler(async () =>
        {
            var tasks = await AppContext.Client.GetAsync<List<ScheduledTaskDto>>("api/scheduler");
            Fmt.RenderList(tasks,
                ["Id", "Name", "Cron", "Active", "Next Run", "Last Run"],
                s => [
                    Fmt.ShortGuid(s.Id),
                    Truncate(s.Name, 30),
                    s.CronExpression,
                    s.IsActive ? "Yes" : "No",
                    Fmt.FormatDate(s.NextRunAt),
                    Fmt.FormatDate(s.LastRunAt)
                ]);
        });
        return cmd;
    }

    private static Command CreateShow()
    {
        var idArg = new Argument<Guid>("id", "Scheduled task ID");
        var cmd = new Command("show", "Show schedule details") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            var task = await AppContext.Client.GetAsync<ScheduledTaskDto>($"api/scheduler/{id}");
            if (AppContext.OutputFormat != "table")
            {
                Fmt.Render(task);
                return;
            }
            var rows = new List<(string, string)>
            {
                ("Id", task.Id.ToString()),
                ("Name", task.Name),
                ("Description", task.Description ?? "-"),
                ("Cron", task.CronExpression),
                ("Timezone", task.Timezone ?? "UTC"),
                ("Active", task.IsActive ? "Yes" : "No"),
                ("Recurring", task.IsRecurring ? "Yes" : "No"),
                ("Occurrences", $"{task.OccurrenceCount}" +
                    (task.MaxOccurrences.HasValue ? $" / {task.MaxOccurrences}" : "")),
                ("Next Run", Fmt.FormatDate(task.NextRunAt)),
                ("Last Run", Fmt.FormatDate(task.LastRunAt)),
                ("Prompt", task.Prompt ?? "-"),
                ("Created By", task.CreatedBy),
                ("Created", Fmt.FormatDate(task.CreatedAt)),
            };
            if (task.TaskTemplate is { } tmpl)
            {
                rows.Add(("Task Title", tmpl.Title));
                rows.Add(("Task Type", tmpl.Type));
                rows.Add(("Task Priority", tmpl.Priority));
                rows.Add(("Task Assignee", tmpl.Assignee));
                rows.Add(("Requires Approval", tmpl.RequiresApproval ? "Yes" : "No"));
            }
            Fmt.RenderDetail([.. rows]);
        }, idArg);
        return cmd;
    }

    private static Command CreateNew()
    {
        var nameOption = new Option<string>("--name", "Schedule name") { IsRequired = true };
        var cronOption = new Option<string>("--cron", "Cron expression (UTC)") { IsRequired = true };
        var taskTitleOption = new Option<string>("--task-title", "Task title") { IsRequired = true };
        var descOption = new Option<string?>("--description", "Schedule description");
        var promptOption = new Option<string?>("--prompt", "LLM prompt for execution");
        var taskDescOption = new Option<string?>("--task-description", "Task description");
        var taskTypeOption = new Option<string?>("--task-type", "Task type");
        taskTypeOption.AddCompletions("General", "SendMessage", "SystemChange", "SkillExecution");
        var taskPriorityOption = new Option<string?>("--task-priority", "Task priority");
        taskPriorityOption.AddCompletions("Low", "Normal", "High", "Urgent");
        var recurringOption = new Option<bool>("--recurring", () => true, "Is recurring");
        var maxOccOption = new Option<int?>("--max-occurrences", "Maximum occurrences");

        var cmd = new Command("create", "Create a scheduled task");
        cmd.AddOption(nameOption);
        cmd.AddOption(cronOption);
        cmd.AddOption(taskTitleOption);
        cmd.AddOption(descOption);
        cmd.AddOption(promptOption);
        cmd.AddOption(taskDescOption);
        cmd.AddOption(taskTypeOption);
        cmd.AddOption(taskPriorityOption);
        cmd.AddOption(recurringOption);
        cmd.AddOption(maxOccOption);
        cmd.SetHandler(async (context) =>
        {
            var pr = context.ParseResult;
            var body = new Dictionary<string, object?>
            {
                ["name"] = pr.GetValueForOption(nameOption)!,
                ["cronExpression"] = pr.GetValueForOption(cronOption)!,
                ["taskTitle"] = pr.GetValueForOption(taskTitleOption)!,
                ["isRecurring"] = pr.GetValueForOption(recurringOption),
            };

            var desc = pr.GetValueForOption(descOption);
            if (desc is not null) body["description"] = desc;
            var prompt = pr.GetValueForOption(promptOption);
            if (prompt is not null) body["prompt"] = prompt;
            var taskDesc = pr.GetValueForOption(taskDescOption);
            if (taskDesc is not null) body["taskDescription"] = taskDesc;
            var taskType = pr.GetValueForOption(taskTypeOption);
            if (taskType is not null) body["taskType"] = taskType;
            var taskPriority = pr.GetValueForOption(taskPriorityOption);
            if (taskPriority is not null) body["taskPriority"] = taskPriority;
            var maxOcc = pr.GetValueForOption(maxOccOption);
            if (maxOcc.HasValue) body["maxOccurrences"] = maxOcc.Value;

            var task = await AppContext.Client.PostAsync<ScheduledTaskDto>("api/scheduler", body);
            Fmt.Success($"Schedule created: {Fmt.ShortGuid(task.Id)} - {task.Name}");
        });
        return cmd;
    }

    private static Command CreatePause()
    {
        var idArg = new Argument<Guid>("id", "Scheduled task ID");
        var cmd = new Command("pause", "Pause a scheduled task") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            await AppContext.Client.PostAsync<ScheduledTaskDto>($"api/scheduler/{id}/disable");
            Fmt.Success($"Schedule {Fmt.ShortGuid(id)} paused");
        }, idArg);
        return cmd;
    }

    private static Command CreateResume()
    {
        var idArg = new Argument<Guid>("id", "Scheduled task ID");
        var cmd = new Command("resume", "Resume a scheduled task") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            await AppContext.Client.PostAsync<ScheduledTaskDto>($"api/scheduler/{id}/enable");
            Fmt.Success($"Schedule {Fmt.ShortGuid(id)} resumed");
        }, idArg);
        return cmd;
    }

    private static Command CreateTrigger()
    {
        var idArg = new Argument<Guid>("id", "Scheduled task ID");
        var cmd = new Command("trigger", "Run a scheduled task immediately") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            await AppContext.Client.PostAsync<object>($"api/scheduler/{id}/run");
            Fmt.Success($"Schedule {Fmt.ShortGuid(id)} triggered");
        }, idArg);
        return cmd;
    }

    private static Command CreateDelete()
    {
        var idArg = new Argument<Guid>("id", "Scheduled task ID");
        var cmd = new Command("delete", "Delete a scheduled task") { idArg };
        cmd.SetHandler(async (Guid id) =>
        {
            await AppContext.Client.DeleteAsync($"api/scheduler/{id}");
            Fmt.Success($"Schedule {Fmt.ShortGuid(id)} deleted");
        }, idArg);
        return cmd;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";
}
