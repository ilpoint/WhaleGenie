using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Editing;

// The read-only probes beside an element field: what the step means, and what the page or the
// desktop answers for that selector without running the step.
internal sealed partial class HeadlessActionSession
{
    public string FieldText(string name)
        => _form.Parameters.FirstOrDefault(parameter =>
               string.Equals(parameter.Definition.Name, name, StringComparison.Ordinal))
           ?.CurrentText.Trim() ?? string.Empty;

    public WindowRule TestWindowRule(string name) => _form.WindowRuleFor(name);

    public EditorActionFormSnapshot SayNarrow(string name, int index, HighlightResult found)
    {
        var field = Field(name);
        if (field.IsNarrow && index >= 0 && index < field.NarrowRows.Count)
        {
            field.NarrowRows[index].Say(found);
        }

        return Snapshot();
    }

    public PageTarget BrowserTarget(string selectorName, int? upto = null)
    {
        var selectors = FieldText(selectorName);
        var match = int.TryParse(FieldText("matchIndex"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var index) && index >= 1 ? index : 1;
        var conditions = new List<PageCondition>();
        var narrow = _form.Parameters.FirstOrDefault(parameter => parameter.IsNarrow);
        var rows = narrow?.NarrowRows ?? [];
        for (var at = 0; at < rows.Count; at++)
        {
            if (upto is { } limit && at > limit)
            {
                break;
            }

            if (rows[at].ToCondition() is not { } condition
                || !Enum.TryParse<PageFilter>(condition.How, ignoreCase: true, out var how))
            {
                continue;
            }

            conditions.Add(new PageCondition(how, condition.Value));
        }

        return PageTarget.Of(selectors, match, conditions);
    }
}
