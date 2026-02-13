using System.Text.RegularExpressions;

namespace Botty.Hooks.Models;

/// <summary>
/// Condition for whether a hook should run (property matching and simple logic).
/// </summary>
public class HookCondition
{
    public Dictionary<string, string>? PropertyEquals { get; set; }
    public Dictionary<string, string>? PropertyContains { get; set; }
    public Dictionary<string, string>? PropertyMatches { get; set; }

    public List<HookCondition>? And { get; set; }
    public List<HookCondition>? Or { get; set; }
    public HookCondition? Not { get; set; }

    public bool Evaluate(HookContext context)
    {
        if (PropertyEquals != null)
        {
            foreach (var (key, expected) in PropertyEquals)
            {
                var actual = context.GetProperty(key);
                if (actual != expected) return false;
            }
        }

        if (PropertyContains != null)
        {
            foreach (var (key, substring) in PropertyContains)
            {
                var actual = context.GetProperty(key);
                if (actual == null || !actual.Contains(substring, StringComparison.OrdinalIgnoreCase)) return false;
            }
        }

        if (PropertyMatches != null)
        {
            foreach (var (key, pattern) in PropertyMatches)
            {
                var actual = context.GetProperty(key);
                if (actual == null || !Regex.IsMatch(actual, pattern)) return false;
            }
        }

        if (And != null && !And.All(c => c.Evaluate(context))) return false;
        if (Or != null && !Or.Any(c => c.Evaluate(context))) return false;
        if (Not != null && Not.Evaluate(context)) return false;

        return true;
    }
}
