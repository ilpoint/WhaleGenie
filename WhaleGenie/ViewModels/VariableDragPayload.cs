using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using WhaleGenie.Core.Execution;

using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>One field a record variable can hand to a scalar parameter.</summary>
public sealed record VariableDragField(string Name, ActionReturnType Type);

/// <summary>
/// A variable on its way to a field. The custom format carries the value shape and record fields;
/// plain text remains a fallback for drags started outside the variable wall.
/// </summary>
public sealed record VariableDragPayload(
    string Name,
    string Type,
    IReadOnlyList<VariableDragField> Fields)
{
    private static readonly DataFormat<VariableDragPayload> Format =
        DataFormat.CreateInProcessFormat<VariableDragPayload>("whalegenie.variable");

    internal static DataFormat<VariableDragPayload> InProcessFormat => Format;

    public string Token => "$" + Name;

    public static VariableDragPayload From(VariableChoice choice) => new(
        choice.Name,
        choice.Type,
        [.. choice.Fields.Select(field =>
            new VariableDragField(field.Name, field.Type))]);

    public DataTransfer Transfer()
    {
        var item = DataTransferItem.Create(Format, this);
        // The text fallback keeps ordinary text targets working while the in-process format carries
        // the exact managed payload, including its shape and record fields.
        item.SetText("var:" + Name);
        var transfer = new DataTransfer();
        transfer.Add(item);
        return transfer;
    }

    public static VariableDragPayload? Read(IDataTransfer transfer)
    {
        if (transfer.TryGetValue(Format) is { } payload)
        {
            return payload;
        }

        var text = transfer.TryGetText()?.Trim();
        if (text is { Length: > 0 })
        {
            if (text.StartsWith("var:", StringComparison.Ordinal))
            {
                text = text[4..];
            }

            if (text.StartsWith('$'))
            {
                text = text[1..];
            }

            return text.Length > 0
                ? new VariableDragPayload(text, string.Empty, [])
                : null;
        }

        return null;
    }
}
