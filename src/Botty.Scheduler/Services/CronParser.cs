using Botty.Core.Interfaces;
using Cronos;
using Microsoft.Extensions.Logging;

namespace Botty.Scheduler.Services;

/// <summary>
/// Cron expression parser using Cronos library.
/// Supports standard 5-field cron expressions (minute, hour, day, month, day-of-week).
/// </summary>
public class CronParser : ICronParser
{
    private readonly ILogger<CronParser> _logger;

    public CronParser(ILogger<CronParser> logger)
    {
        _logger = logger;
    }

    public DateTime? GetNextOccurrence(string cronExpression, DateTime after)
    {
        try
        {
            var expression = CronExpression.Parse(cronExpression);
            var next = expression.GetNextOccurrence(after, TimeZoneInfo.Utc);
            return next;
        }
        catch (CronFormatException ex)
        {
            _logger.LogWarning(ex, "Invalid cron expression: {Expression}", cronExpression);
            return null;
        }
    }

    public bool IsValid(string cronExpression)
    {
        try
        {
            CronExpression.Parse(cronExpression);
            return true;
        }
        catch (CronFormatException ex)
        {
            _logger.LogWarning(ex, "Invalid cron expression: {Expression}", cronExpression);
            return false;
        }
    }

    public IEnumerable<DateTime> GetOccurrences(string cronExpression, DateTime start, DateTime end)
    {
        try
        {
            var expression = CronExpression.Parse(cronExpression);
            return expression.GetOccurrences(start, end, TimeZoneInfo.Utc);
        }
        catch (CronFormatException ex)
        {
            _logger.LogWarning(ex, "Invalid cron expression: {Expression}", cronExpression);
            return [];
        }
    }
}
