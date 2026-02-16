using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Botty.Skills.Browser;

/// <summary>
/// Transforms Playwright's YAML aria snapshot into an annotated tree with element refs.
/// </summary>
public static partial class AccessibilitySnapshotBuilder
{
    private static readonly HashSet<string> InteractiveRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "button", "link", "textbox", "checkbox", "radio", "combobox", "slider",
        "spinbutton", "switch", "tab", "menuitem", "option", "searchbox",
        "menuitemcheckbox", "menuitemradio", "treeitem"
    };

    private static readonly HashSet<string> NamedContentRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "heading", "cell", "columnheader", "rowheader", "img", "figure"
    };

    // Matches lines like: "  - button \"Submit\"" or "  - heading \"Title\" [level=2]"
    [GeneratedRegex(@"^(\s*-\s+)(\w+)(?:\s+""([^""]*)"")?(.*)", RegexOptions.Compiled)]
    private static partial Regex AriaLineRegex();

    /// <summary>
    /// Builds an annotated accessibility snapshot with element refs.
    /// </summary>
    public static (string Snapshot, Dictionary<string, ElementRef> Refs, bool Truncated) Build(
        string ariaYaml,
        bool interactiveOnly,
        int maxChars = 80000)
    {
        if (string.IsNullOrWhiteSpace(ariaYaml))
        {
            return ("(empty page)", new Dictionary<string, ElementRef>(), false);
        }

        var refs = new Dictionary<string, ElementRef>();
        var nthTracker = new Dictionary<string, int>(StringComparer.Ordinal);
        var refCounter = 0;
        var sb = new StringBuilder();
        var truncated = false;

        foreach (var line in ariaYaml.Split('\n'))
        {
            var match = AriaLineRegex().Match(line);
            if (!match.Success)
            {
                AppendLine(sb, line, maxChars, ref truncated);
                continue;
            }

            var indent = match.Groups[1].Value;
            var role = match.Groups[2].Value;
            var name = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;
            var rest = match.Groups[4].Value;

            var isInteractive = IsInteractiveRole(role);
            var isNamedContent = NamedContentRoles.Contains(role) && !string.IsNullOrEmpty(name);

            if (interactiveOnly && !isInteractive)
            {
                continue;
            }

            if (isInteractive || isNamedContent)
            {
                refCounter++;
                var refId = $"e{refCounter}";
                var nthKey = $"{role.ToLowerInvariant()}|{name}";

                if (!nthTracker.TryGetValue(nthKey, out var nth))
                {
                    nth = 0;
                }
                nthTracker[nthKey] = nth + 1;

                refs[refId] = new ElementRef(role, name, nth);

                var annotatedLine = !string.IsNullOrEmpty(name)
                    ? $"{indent}{role} \"{name}\" [ref={refId}]{rest}"
                    : $"{indent}{role} [ref={refId}]{rest}";
                AppendLine(sb, annotatedLine, maxChars, ref truncated);
            }
            else
            {
                AppendLine(sb, line, maxChars, ref truncated);
            }

            if (truncated) break;
        }

        return (sb.ToString().TrimEnd(), refs, truncated);
    }

    /// <summary>
    /// Resolves a ref ID to a Playwright locator.
    /// </summary>
    public static ILocator ResolveRefToLocator(IPage page, string refId, Dictionary<string, ElementRef> refs)
    {
        if (!refs.TryGetValue(refId, out var elementRef))
        {
            throw new ArgumentException($"Unknown ref '{refId}'. Call browser_snapshot first to get current refs.");
        }

        var role = ParseAriaRole(elementRef.Role);
        var locator = string.IsNullOrEmpty(elementRef.Name)
            ? page.GetByRole(role)
            : page.GetByRole(role, new PageGetByRoleOptions { Name = elementRef.Name, Exact = true });

        if (elementRef.Nth > 0)
        {
            locator = locator.Nth(elementRef.Nth);
        }

        return locator;
    }

    public static bool IsInteractiveRole(string role) => InteractiveRoles.Contains(role);

    private static void AppendLine(StringBuilder sb, string line, int maxChars, ref bool truncated)
    {
        if (truncated) return;

        if (sb.Length + line.Length + 1 > maxChars)
        {
            truncated = true;
            sb.AppendLine("... (truncated)");
            return;
        }

        sb.AppendLine(line);
    }

    private static AriaRole ParseAriaRole(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "button" => AriaRole.Button,
            "link" => AriaRole.Link,
            "textbox" => AriaRole.Textbox,
            "checkbox" => AriaRole.Checkbox,
            "radio" => AriaRole.Radio,
            "combobox" => AriaRole.Combobox,
            "slider" => AriaRole.Slider,
            "spinbutton" => AriaRole.Spinbutton,
            "switch" => AriaRole.Switch,
            "tab" => AriaRole.Tab,
            "menuitem" => AriaRole.Menuitem,
            "option" => AriaRole.Option,
            "searchbox" => AriaRole.Searchbox,
            "menuitemcheckbox" => AriaRole.Menuitem,
            "menuitemradio" => AriaRole.Menuitem,
            "treeitem" => AriaRole.Treeitem,
            "heading" => AriaRole.Heading,
            "cell" => AriaRole.Cell,
            "columnheader" => AriaRole.Columnheader,
            "rowheader" => AriaRole.Rowheader,
            "img" => AriaRole.Img,
            "figure" => AriaRole.Figure,
            _ => AriaRole.Generic
        };
    }
}
