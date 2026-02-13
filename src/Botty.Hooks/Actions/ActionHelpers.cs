using System.Text.Json;
using System.Text.RegularExpressions;
using Botty.Hooks.Models;

namespace Botty.Hooks.Actions;

internal static class ActionHelpers
{
    /// <summary>
    /// Replaces {{path}} in template with context.Payload path value.
    /// </summary>
    public static string SubstituteVariables(string template, HookContext context)
    {
        if (string.IsNullOrEmpty(template)) return template;
        return Regex.Replace(template, @"\{\{(\w+(?:\.\w+)*)\}\}", m =>
        {
            var path = m.Groups[1].Value;
            var value = context.GetProperty(path);
            return value ?? m.Value;
        });
    }
}
