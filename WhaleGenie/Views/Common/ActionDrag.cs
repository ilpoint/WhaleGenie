using System;
using Avalonia.Input;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>What a card on the left sends to the step list.</summary>
internal sealed record ActionDragPayload(ActionPaletteCardKind Kind, string Key, int Version = 0)
{
    private const string MarkName = "whalegenie.action";
    private static readonly DataFormat<string> Mark =
        DataFormat.CreateStringApplicationFormat(MarkName);

    public DataTransfer Transfer()
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(Mark,
            $"{Kind}|{Key}|{Version.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        return transfer;
    }

    public static ActionDragPayload? Read(IDataTransfer transfer)
    {
        if (transfer.TryGetValue(Mark) is not { } text)
        {
            return null;
        }

        var parts = text.Split('|', 3);
        if (parts.Length != 3
            || !Enum.TryParse<ActionPaletteCardKind>(parts[0], out var kind)
            || !int.TryParse(parts[2], out var version))
        {
            return null;
        }

        return new ActionDragPayload(kind, parts[1], version);
    }
}
